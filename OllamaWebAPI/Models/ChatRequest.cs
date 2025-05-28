
public class ChatRequest
{
    public string model { get; set; } = "";
    public List<Message> messages { get; set; } = new List<Message>();
}
