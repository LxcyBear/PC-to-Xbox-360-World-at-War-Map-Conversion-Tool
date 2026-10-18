using System.Collections.ObjectModel;
using WaW360.Core.FastFiles;

namespace WaW360.Core.Linker;

public enum T4LinkAssetOrigin
{
    PcTopLevel,
    PcNested,
    Iwd,
    XboxDonor,
    Generated
}

public enum T4LinkAssetState
{
    Parsed,
    ConversionRequired,
    DonorRequired,
    Generated,
    Ready,
    Blocked
}

public enum T4LinkDependencyKind
{
    TechniqueSet,
    TextureImage,
    Material,
    XModel,
    LoadedSound,
    World,
    Other
}

public readonly record struct T4LinkAssetKey(string TypeName, string Name)
{
    public static T4LinkAssetKey Create(string typeName, string name)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("Asset type is required.", nameof(typeName));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Asset name is required.", nameof(name));
        return new T4LinkAssetKey(typeName.Trim().ToLowerInvariant(), name.Trim());
    }

    public override string ToString() => $"{TypeName}:{Name}";
}

public sealed record T4LinkProvenance(
    T4LinkAssetOrigin Origin,
    string Source,
    string SourceSha256,
    int? SourceAssetIndex,
    int? SourceOffset,
    int? SourceEnd,
    string? PayloadSha256,
    string Note);

public sealed record T4LinkDependency(
    T4LinkAssetKey Target,
    T4LinkDependencyKind Kind,
    int Ordinal,
    string Relationship,
    bool Required = true);

public sealed class T4LinkAssetNode
{
    private readonly List<T4LinkDependency> _dependencies = new();
    private readonly List<T4LinkProvenance> _provenance = new();
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    public required T4LinkAssetKey Key { get; init; }
    public required T4LinkAssetOrigin Origin { get; set; }
    public required T4LinkAssetState State { get; set; }
    public int? PcTypeId { get; set; }
    public int XboxTypeId { get; set; }
    public int FirstSeenOrder { get; set; }
    public bool IsRoot { get; set; }
    public string? BlockReason { get; set; }

    public IReadOnlyList<T4LinkDependency> Dependencies => _dependencies;
    public IReadOnlyList<T4LinkProvenance> Provenance => _provenance;
    public IReadOnlyDictionary<string, string> Metadata => new ReadOnlyDictionary<string, string>(_metadata);

    public void AddDependency(T4LinkDependency dependency)
    {
        if (_dependencies.Any(d =>
                T4LinkAssetKeyComparer.Instance.Equals(d.Target, dependency.Target) &&
                d.Kind == dependency.Kind &&
                d.Ordinal == dependency.Ordinal &&
                d.Relationship.Equals(dependency.Relationship, StringComparison.OrdinalIgnoreCase)))
            return;
        _dependencies.Add(dependency);
    }

    public void AddProvenance(T4LinkProvenance provenance)
    {
        if (_provenance.Any(p =>
                p.Origin == provenance.Origin &&
                string.Equals(p.Source, provenance.Source, StringComparison.OrdinalIgnoreCase) &&
                p.SourceAssetIndex == provenance.SourceAssetIndex &&
                p.SourceOffset == provenance.SourceOffset &&
                p.SourceEnd == provenance.SourceEnd))
            return;
        _provenance.Add(provenance);
    }

    public void SetMetadata(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Metadata key is required.", nameof(key));
        _metadata[key] = value ?? string.Empty;
    }
}

public sealed class T4LinkGraph
{
    private readonly Dictionary<T4LinkAssetKey, T4LinkAssetNode> _nodes = new(T4LinkAssetKeyComparer.Instance);
    private readonly List<T4LinkAssetKey> _roots = new();
    private int _nextOrder;

    public IReadOnlyCollection<T4LinkAssetNode> Nodes => _nodes.Values;
    public IReadOnlyList<T4LinkAssetKey> Roots => _roots;

    public T4LinkAssetNode AddOrMerge(
        T4LinkAssetKey key,
        T4LinkAssetOrigin origin,
        T4LinkAssetState state,
        int? pcTypeId = null,
        bool isRoot = false)
    {
        if (!_nodes.TryGetValue(key, out var node))
        {
            node = new T4LinkAssetNode
            {
                Key = key,
                Origin = origin,
                State = state,
                PcTypeId = pcTypeId,
                XboxTypeId = T4XboxAssetTypeMap.GetXboxTypeId(key.TypeName),
                FirstSeenOrder = _nextOrder++,
                IsRoot = isRoot
            };
            _nodes.Add(key, node);
        }
        else
        {
            if (isRoot) node.IsRoot = true;
            if (node.PcTypeId is null && pcTypeId is not null) node.PcTypeId = pcTypeId;
            if ((int)state > (int)node.State) node.State = state;
            if ((int)origin < (int)node.Origin) node.Origin = origin;
        }

        if (isRoot && !_roots.Any(r => T4LinkAssetKeyComparer.Instance.Equals(r, key)))
            _roots.Add(key);
        return node;
    }

    public T4LinkAssetNode GetRequired(T4LinkAssetKey key) =>
        _nodes.TryGetValue(key, out var node)
            ? node
            : throw new InvalidDataException($"Link graph does not contain required asset '{key}'.");

    public void AddDependency(T4LinkAssetKey parent, T4LinkDependency dependency)
    {
        if (!_nodes.ContainsKey(dependency.Target))
            throw new InvalidDataException($"Cannot add dependency {parent} -> {dependency.Target}: target node does not exist.");
        GetRequired(parent).AddDependency(dependency);
    }

    /// <summary>
    /// Produces CODXe-style dependency-first order. Each root keeps source/root order;
    /// newly closed dependencies are emitted immediately before the parent that first
    /// requires them. Already-emitted dependencies are not duplicated.
    /// </summary>
    public IReadOnlyList<T4LinkAssetNode> BuildDependencyFirstOrder()
    {
        var ordered = new List<T4LinkAssetNode>(_nodes.Count);
        var emitted = new HashSet<T4LinkAssetKey>(T4LinkAssetKeyComparer.Instance);
        var visiting = new HashSet<T4LinkAssetKey>(T4LinkAssetKeyComparer.Instance);

        void Visit(T4LinkAssetKey key)
        {
            if (emitted.Contains(key)) return;
            if (!visiting.Add(key))
                throw new InvalidDataException($"Asset dependency cycle detected while linking '{key}'.");

            var node = GetRequired(key);
            foreach (var dep in node.Dependencies
                         .OrderBy(d => d.Ordinal)
                         .ThenBy(d => d.Target.TypeName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(d => d.Target.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (dep.Required) Visit(dep.Target);
            }

            visiting.Remove(key);
            if (emitted.Add(key)) ordered.Add(node);
        }

        foreach (var root in _roots) Visit(root);
        foreach (var node in _nodes.Values.OrderBy(n => n.FirstSeenOrder)) Visit(node.Key);
        return ordered;
    }

}

internal sealed class T4LinkAssetKeyComparer : IEqualityComparer<T4LinkAssetKey>
{
    public static readonly T4LinkAssetKeyComparer Instance = new();

    public bool Equals(T4LinkAssetKey x, T4LinkAssetKey y) =>
        string.Equals(x.TypeName, y.TypeName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(T4LinkAssetKey obj) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TypeName ?? string.Empty),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name ?? string.Empty));
}
