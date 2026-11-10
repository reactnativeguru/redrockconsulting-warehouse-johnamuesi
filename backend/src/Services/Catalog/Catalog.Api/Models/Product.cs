using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Catalog.Api.Models;

/// <summary>
/// A product document in MongoDB.
///
/// Why a document DB for the catalogue? Products are read-heavy, naturally
/// hierarchical (variants, attributes) and their shape varies by category -
/// a tent has "berths", a boot has "sizes". A document stores the whole product
/// in one record with no joins, and new attributes need no schema migration.
/// </summary>
public class Product
{
    /// <summary>The SKU is the natural key, so we use it as Mongo's _id.</summary>
    [BsonId]
    public required string Sku { get; set; }

    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public required string Category { get; set; }

    /// <summary>Stored as Decimal128 - never use double for money.</summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    /// <summary>EAN-13 barcode printed on the label - what the handheld scans.</summary>
    public required string Barcode { get; set; }

    public List<string> Colours { get; set; } = [];
    public List<string> Sizes { get; set; } = [];

    /// <summary>Category-specific attributes, e.g. { "waterproofRating": "10000mm" }.</summary>
    public Dictionary<string, string> Attributes { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
