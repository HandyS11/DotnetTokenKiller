namespace SampleApp.EfCore;

/// <summary>A simple entity used as an EF Core model for integration test scaffolding.</summary>
public class SampleItem
{
    /// <summary>Gets or sets the primary key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the item name.</summary>
    public string Name { get; set; } = string.Empty;
}
