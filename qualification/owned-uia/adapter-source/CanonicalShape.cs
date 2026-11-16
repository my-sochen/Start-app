using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace StartApp.Observation.Contracts;

// Byte projection only. No attempt/phase freshness or effect authorization is created here.
public sealed record CanonicalRow(int parent, string type, string Name, string ClassName, string AutomationId, int ProcessId, int NativeWindowHandle);
// Freely constructible projection data is NOT owner-bound evidence.
public sealed record CanonicalProjection(Guid AttemptId, Guid AcquisitionId, Phase Phase, SampleInterval Interval, ImmutableArray<byte> Utf8, string UpperSha256);
public static class CanonicalShape
{
    public static CanonicalProjection Project(Snapshot snapshot)
    {
        if (snapshot.Plan != Plan.CanonicalShape || snapshot.Phase is not (Phase.PreConnect or Phase.OffPrecheck)) throw new InvalidOperationException("Shape-only completed acquisition required");
        var rows = snapshot.Rows.Select(r => new CanonicalRow(r.Parent, Required<string>(r, FieldId.ProgrammaticName), Required<string>(r, FieldId.Name), Required<string>(r, FieldId.ClassName), Required<string>(r, FieldId.AutomationId), Required<int>(r, FieldId.ShapeProcessId), Required<int>(r, FieldId.Hwnd)));
        byte[] bytes = Serialize(rows);
        return new(snapshot.AttemptId, snapshot.AcquisitionId, snapshot.Phase, snapshot.Interval, ImmutableArray.CreateRange(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
    }
    static T Required<T>(ObservedRow row, FieldId field) => row.Get<T>(field).TryGet(out var value) ? value! : throw new InvalidOperationException("Required canonical field unavailable");
    // Exposed pure wire function supports golden testing; arbitrary rows carry NO provenance.
    public static byte[] Serialize(IEnumerable<CanonicalRow> rows) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows));
}

public sealed class BoundCanonicalDigest { public CanonicalProjection Projection { get; } internal BoundCanonicalDigest(CanonicalProjection projection) { Projection = projection; } }
