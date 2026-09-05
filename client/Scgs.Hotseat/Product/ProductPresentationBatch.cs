// SPDX-License-Identifier: GPL-3.0-or-later
using V05 = Scgs.Client.V05;

namespace Scgs.Hotseat.Product;

/// <summary>
/// A public, authoritative observation. It deliberately carries neither a
/// legacy event's text nor a command (which can contain private choice IDs).
/// Sequence is presentation deduplication identity, not a viewer ACK cursor.
/// </summary>
public sealed record ProductPresentationObservation(
    ulong Sequence,
    V05.ProductEventObservation Observation);

/// <summary>
/// The only data handed across the resolving/presentation boundary. Both board
/// projections omit hands and anonymize face-down tactics, including our own.
/// </summary>
public sealed record ProductPresentationBatch
{
    internal ProductPresentationBatch(
        ulong id,
        V05.PlayerId perspectivePlayer,
        ProductHotseatPublicBoardView before,
        ProductHotseatPublicBoardView after,
        IEnumerable<ProductPresentationObservation> observations)
    {
        Id = id;
        PerspectivePlayer = perspectivePlayer;
        Before = before;
        After = after;
        Observations = Array.AsReadOnly(observations.ToArray());
    }

    public ulong Id { get; }
    public ulong PreviousRevision => Before.Revision;
    public ulong Revision => After.Revision;
    public V05.PlayerId PerspectivePlayer { get; }
    public ProductHotseatPublicBoardView Before { get; }
    public ProductHotseatPublicBoardView After { get; }
    public IReadOnlyList<ProductPresentationObservation> Observations { get; }
}
