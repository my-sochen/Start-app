using System.Text;
using System.Text.Json.Nodes;
using StartApp.Presentation;

namespace StartApp.Presentation.Tests;

public sealed class ApprovedCatalogContractTests
{
    private static readonly DateTimeOffset Clock = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly CatalogPolicy Policy = new(65536, 128, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
    private static JsonObject Example() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "update-fixtures", "catalog.synthetic.example.json")))!.AsObject();
    private static byte[] Bytes(JsonNode doc) => Encoding.UTF8.GetBytes(doc.ToJsonString());
    private static ValidatedCatalog Parse(JsonNode doc) => ApprovedCatalogContract.Parse(Bytes(doc), Clock, Policy);
    [Fact] public void Shared_version_fixtures_match_application_selection()
    {
        var fixtures = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "update-fixtures", "version-selection.fixtures.json")))!;
        foreach (var p in fixtures["ordering"]!.AsArray()) Assert.True(UpdateVersion.Parse((string)p!["newer"]!)!.CompareTo(UpdateVersion.Parse((string)p["older"]!)) > 0);
        foreach (var p in fixtures["invalid_versions"]!.AsArray()) Assert.Null(UpdateVersion.Parse((string)p!));
        foreach (var p in fixtures["selection"]!.AsArray())
        {
            var doc = Example(); var template = doc["releases"]![0]!.DeepClone(); doc["releases"] = new JsonArray(); var id = 0;
            foreach (var v in p!["eligible"]!.AsArray())
            {
                var record = template.DeepClone(); record["version"] = (string)v!; record["tag"] = "v" + (string)v!; record["asset_id"] = ++id;
                doc["releases"]!.AsArray().Add(record);
            }
            Assert.Equal((string?)p["expected"], Parse(doc).SelectNewer((string)p["installed"]!)?.Version);
        }
    }
    [Fact] public void Synthetic_reference_validates_syntax_not_approval() => Assert.Equal("synthetic-approval-not-real", Parse(Example()).Releases[0].ApprovalRecordId);
    [Fact] public void Empty_catalog_does_not_claim_current() { var d=Example(); d["releases"]=new JsonArray(); Assert.Null(Parse(d).SelectNewer("0.1.0-proto.1")); }
    [Theory]
    [InlineData("schema", "2")][InlineData("schema", "true")][InlineData("channel", "\"stable\"")][InlineData("revision", "0")]
    [InlineData("generated_utc", "\"2026-11-18T00:00:00Z\"")][InlineData("expires_utc", "\"2026-11-17T12:00:00Z\"")]
    [InlineData("generated_utc", "\"2026-02-30T00:00:00Z\"")][InlineData("generated_utc", "\"2026-11-17T00:00:00+00:00\"")]
    public void Invalid_envelope_fails_closed(string field,string value) { var d=Example();d[field]=JsonNode.Parse(value);Assert.Throws<InvalidDataException>(()=>Parse(d)); }
    [Theory]
    [InlineData("size", "true")][InlineData("size", "1.0")][InlineData("size", "-1")][InlineData("release_id", "0")]
    [InlineData("tag", "\"v9.9.9\"")][InlineData("commit", "\"short\"")][InlineData("sha256", "\"short\"")]
    [InlineData("layout", "\"legacy\"")][InlineData("approval_record_id", "\"https://untrusted.invalid/\"")]
    public void Invalid_record_fails_closed(string field,string value) {var d=Example();d["releases"]![0]![field]=JsonNode.Parse(value);Assert.Throws<InvalidDataException>(()=>Parse(d));}
    [Fact] public void Forged_approved_boolean_is_rejected() {var d=Example();d["releases"]![0]!["approved"]=true;Assert.Throws<InvalidDataException>(()=>Parse(d));}
    [Fact] public void Duplicate_version_or_asset_is_rejected() {var d=Example();d["releases"]!.AsArray().Add(d["releases"]![0]!.DeepClone());Assert.Throws<InvalidDataException>(()=>Parse(d));}
    [Fact] public void Duplicate_JSON_properties_rejected() => Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Encoding.UTF8.GetBytes("{\"schema\":1,\"schema\":1}"),Clock,Policy));
    [Fact] public void Byte_limit_enforced() => Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Bytes(Example()),Clock,Policy with {MaximumBytes=3}));
    [Fact] public void Age_and_lifetime_policy_enforced()
    {
        Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Bytes(Example()),Clock,Policy with {MaximumAge=TimeSpan.FromHours(1)}));
        Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Bytes(Example()),Clock,Policy with {MaximumLifetime=TimeSpan.FromHours(1)}));
    }
    [Fact] public void Explicit_minimum_revision_blocks_rollback() => Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Bytes(Example()),Clock,Policy,minimumRevision:2));
    [Theory][InlineData("{")][InlineData("NaN")][InlineData("[]")]
    public void Malformed_JSON_fails_closed(string text) => Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Encoding.UTF8.GetBytes(text),Clock,Policy));
}
