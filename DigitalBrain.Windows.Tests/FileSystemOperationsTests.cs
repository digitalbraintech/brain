using Xunit;

namespace DigitalBrain.Windows.Tests;

public class FileSystemOperationsTests
{
    [Fact]
    public async Task Write_Read_List_Delete_RoundTrip()
    {
        var fs = new FileSystemOperations();
        var dir = Path.Combine(Path.GetTempPath(), "dbfsops-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "note.txt");
        try
        {
            await fs.WriteFileAsync(file, "hello fs");
            Assert.True(await fs.ExistsAsync(file));
            Assert.Equal("hello fs", await fs.ReadFileAsync(file));
            Assert.Contains(file, await fs.ListFilesAsync(dir, "*.txt"));
            await fs.DeleteAsync(file);
            Assert.False(await fs.ExistsAsync(file));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
