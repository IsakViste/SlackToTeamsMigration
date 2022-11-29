namespace STMigration.Models;

public class ComplexMessage {
    public SimpleMessage Message { get; set; }

    public bool IsThread { get; set; }
    public List<SimpleMessage>? ThreadMessages { get; set; }

    public ComplexMessage(SimpleMessage message, bool isThread) {
        Message = message;
        IsThread = isThread;
        if (IsThread) {
            ThreadMessages = new List<SimpleMessage>();
        }
    }
}
