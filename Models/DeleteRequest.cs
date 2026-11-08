namespace TestProject.Models;

public sealed class DeleteRequest
{
    public string? Path { get; set; }

    /// <summary>
    /// Required true when deleting a non-empty directory.
    /// </summary>
    public bool Recursive { get; set; }
}
