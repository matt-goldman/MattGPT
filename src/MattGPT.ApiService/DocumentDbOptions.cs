namespace MattGPT.ApiService;

/// <summary>Configuration options for the document database provider.</summary>
public class DocumentDbOptions
{
    public const string SectionName = "DocumentDb";

    /// <summary>
    /// The document database provider to use.
    /// Supported values: MongoDB, Postgres.
    /// </summary>
    public string Provider { get; set; } = "MongoDB";
}
