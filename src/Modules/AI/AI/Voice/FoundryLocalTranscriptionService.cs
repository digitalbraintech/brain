using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;

// Local Whisper via Microsoft Foundry Local (IAW port). Owned by the AI module.
public sealed class FoundryLocalTranscriptionService :
    IAudioTranscriptionService,
    IHostedService,
    IAsyncDisposable
{
    public const string DefaultTranscriptionKey = VoiceToTextHosting.DefaultTranscriptionKey;

    // Catalogue order expresses local preference, the job WhisperModel.Priority
    // used to do: best-first, walked when the configured model is absent.
    private static readonly TranscriptionModel[] LocalModels =
        [.. TranscriptionModel.All.Where(static model => model.Provider is AiProvider.FoundryLocal)];

    private static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    private readonly IConfiguration _configuration;
    private readonly IAudioConverter? _audioConverter;
    private readonly ILogger<FoundryLocalTranscriptionService> _logger;
    private IModel? _model;
    private IModel? _cpuFallbackVariant;
    private bool _cudaFailed;

    public FoundryLocalTranscriptionService(
        IConfiguration configuration,
        ILogger<FoundryLocalTranscriptionService> logger,
        IAudioConverter? audioConverter = null)
    {
        _configuration = configuration;
        _logger = logger;
        _audioConverter = audioConverter;
    }

    public bool IsReady { get; private set; }
    public bool InitializationFailed { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string ModelId => _cpuFallbackVariant?.Id ?? _model?.Id ?? "unconfigured";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing Foundry Local for Whisper…");
            await InitializeFoundryManagerAsync().ConfigureAwait(false);

            var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            _model = await ResolveModelAsync(catalog).ConfigureAwait(false);

            _logger.LogInformation("Downloading Whisper model {ModelId}…", _model.Id);
            await WithTimeout(_model.DownloadAsync(), DownloadTimeout, "Whisper model download", cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Loading Whisper model {ModelId}…", _model.Id);
            await WithTimeout(_model.LoadAsync(), LoadTimeout, "Whisper model load", cancellationToken)
                .ConfigureAwait(false);

            IsReady = true;
            _logger.LogInformation("Whisper model ready: {ModelId}", _model.Id);
        }
        catch (Exception ex)
        {
            InitializationFailed = true;
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Whisper initialization failed");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var activeId = _cpuFallbackVariant?.Id ?? _model?.Id;
        if (activeId is null)
        {
            return;
        }

        _logger.LogInformation("Unloading Whisper model {ModelId}", activeId);
        try
        {
            if (_cpuFallbackVariant is not null)
            {
                await _cpuFallbackVariant.UnloadAsync().ConfigureAwait(false);
            }
            else if (_model is not null)
            {
                await _model.UnloadAsync().ConfigureAwait(false);
            }

            _model = null;
            _cpuFallbackVariant = null;
            IsReady = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unloading Whisper model");
        }
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        EnsureReady();

        var extension = Path.GetExtension(audioFilePath).ToLowerInvariant();
        var fileToTranscribe = audioFilePath;
        string? convertedFilePath = null;

        if (OggExtensions.Contains(extension) && _audioConverter is not null)
        {
            _logger.LogDebug("Converting {Source} to WAV", audioFilePath);
            convertedFilePath = _audioConverter.ConvertToWav(audioFilePath);
            fileToTranscribe = convertedFilePath;
        }

        try
        {
            return await RunTranscription(fileToTranscribe, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!_cudaFailed && IsCudaError(ex))
        {
            _logger.LogWarning(ex, "CUDA inference failed on {ModelId}, falling back to CPU", _model!.Id);
            _cudaFailed = true;
            await FallbackToCpuVariantAsync(cancellationToken).ConfigureAwait(false);
            return await RunTranscription(fileToTranscribe, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (convertedFilePath is not null && File.Exists(convertedFilePath))
            {
                File.Delete(convertedFilePath);
            }
        }
    }

    public async Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".wav";
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"db_voice_{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await audioStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            return await TranscribeAsync(tempPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsReady)
        {
            return;
        }

        try
        {
            if (_cpuFallbackVariant is not null)
            {
                await _cpuFallbackVariant.UnloadAsync().ConfigureAwait(false);
            }
            else if (_model is not null)
            {
                await _model.UnloadAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private void EnsureReady()
    {
        if (IsReady)
        {
            return;
        }

        if (InitializationFailed)
        {
            throw new InvalidOperationException($"Whisper is not available: {ErrorMessage}");
        }

        throw new InvalidOperationException("Whisper model is still initializing.");
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
        if (completed != task)
        {
            throw new TimeoutException($"{operation} timed out after {timeout.TotalMinutes:F0} minutes");
        }

        await task.ConfigureAwait(false);
    }

    private async Task InitializeFoundryManagerAsync()
    {
        try
        {
            _ = FoundryLocalManager.Instance;
        }
        catch (FoundryLocalException)
        {
            _logger.LogInformation("Creating Foundry Local manager…");
            var config = new Configuration { AppName = "digitalbrain" };
            await FoundryLocalManager.CreateAsync(config, _logger).ConfigureAwait(false);
        }
    }

    private async Task<string> RunTranscription(string filePath, CancellationToken ct)
    {
        _logger.LogDebug("Transcribing {Path}", filePath);
        var model = _cpuFallbackVariant ?? _model
            ?? throw new InvalidOperationException("Whisper model is not loaded.");

        using var session = new AudioSession(model);
        using var request = new Microsoft.AI.Foundry.Local.Request();
        request.AddItem(new AudioItem(filePath));
        using var response = await session.ProcessRequestAsync(request, ct).ConfigureAwait(false);

        var transcription = response
            .OfType<SpeechResultItem>()
            .Select(static item => item.Text)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text))
            ?.Trim()
            ?? string.Empty;

        _logger.LogInformation("Transcription complete: {Length} chars", transcription.Length);
        return transcription;
    }

    private async Task FallbackToCpuVariantAsync(CancellationToken ct)
    {
        var currentId = _model!.Id;
        try
        {
            await _model.UnloadAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }

        var cpuVariant = _model.Variants
            .FirstOrDefault(v => v.Id.Contains("generic-cpu", StringComparison.OrdinalIgnoreCase));
        if (cpuVariant is null)
        {
            throw new InvalidOperationException(
                $"CUDA inference failed and no CPU variant found for {_model.Alias ?? currentId}");
        }

        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct).ConfigureAwait(false);
        var cpuModel = await catalog.GetModelVariantAsync(cpuVariant.Id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"CPU variant {cpuVariant.Id} not found in Foundry Local catalog");

        await WithTimeout(cpuModel.DownloadAsync(), DownloadTimeout, "CPU model download", ct).ConfigureAwait(false);
        await WithTimeout(cpuModel.LoadAsync(), LoadTimeout, "CPU model load", ct).ConfigureAwait(false);
        _cpuFallbackVariant = cpuModel;
        _logger.LogInformation("CPU fallback model ready: {ModelId}", cpuModel.Id);
    }

    private static bool IsCudaError(Exception ex)
        => ex.ToString().Contains("CUDNN", StringComparison.OrdinalIgnoreCase)
            || ex.ToString().Contains("CUDA", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("OnnxRuntimeGenAI", StringComparison.OrdinalIgnoreCase);

    private async Task<IModel> ResolveModelAsync(ICatalog catalog)
    {
        // The key names a marker; the Foundry catalogue is keyed by wire id.
        var configuredId = TranscriptionModel
            .FindByMarkerName(_configuration[DefaultTranscriptionKey] ?? string.Empty)?.Id;
        if (!string.IsNullOrEmpty(configuredId))
        {
            var configured = await catalog.GetModelAsync(configuredId).ConfigureAwait(false);
            if (configured is not null)
            {
                return configured;
            }

            _logger.LogWarning("Configured whisper model '{ModelId}' not found; falling back", configuredId);
        }

        foreach (var whisperModel in LocalModels)
        {
            var found = await catalog.GetModelAsync(whisperModel.Id).ConfigureAwait(false);
            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException(
            "No whisper model found in Foundry Local catalog. "
            + $"Expected one of: {string.Join(", ", LocalModels.Select(static m => m.Id))}");
    }
}
