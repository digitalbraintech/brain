using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.UI;

namespace DigitalBrain.Chat;

[Alias("chat")]
public partial interface IChat :
    INeuron,
    IHandle<SendMessage>,
    IHandle<CancelTurn>,
    IHandle<ReadTranscriptRequest>,
    IHandle<ReadTurns>,
    IHandle<ReadActiveExecution>,
    IHandle<SetActiveExecution>,
    IHandle<CompleteUserAction>,
    IHandle<Note>,
    IHandle<PublishNote>,
    IHandle<KitCardOffer>;
