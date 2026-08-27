// Core service: number sequences by name.
// Typed contract methods: Next / Preview / Raw (generated INumberSequences).
public partial class NumberSequences
{
    private readonly INumberSequenceService _sequences;
    public NumberSequences(INumberSequenceService sequences) => _sequences = sequences;

    public async Task<string> Next(string sequence, System.Threading.CancellationToken ct = default)
        => await _sequences.NextByNameAsync(sequence, cancellationToken: ct);

    public async Task<string?> Preview(string sequence, System.Threading.CancellationToken ct = default)
        => await _sequences.PreviewByNameAsync(sequence, cancellationToken: ct);

    public async Task<long> Raw(string sequence, System.Threading.CancellationToken ct = default)
        => await _sequences.NextRawByNameAsync(sequence, cancellationToken: ct);
}
