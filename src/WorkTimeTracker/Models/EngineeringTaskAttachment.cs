namespace WorkTimeTracker.Models;

public class EngineeringTaskAttachment
{
    public int Id { get; set; }
    public int EngineeringTaskId { get; set; }
    public EngineeringTask EngineeringTask { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
