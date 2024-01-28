﻿using BymlLibrary.Extensions;
using BymlLibrary.Structures;
using BymlLibrary.Yaml;
using Revrs;
using Revrs.Extensions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BymlLibrary.Nodes.Immutable.Containers;

public readonly ref struct ImmutableBymlMap(Span<byte> data, int offset, int count)
{
    /// <summary>
    /// Span of the BYMl data
    /// </summary>
    private readonly Span<byte> _data = data;

    /// <summary>
    /// The container offset (start of header)
    /// </summary>
    private readonly int _offset = offset;

    /// <summary>
    /// The container item count
    /// </summary>
    private readonly int Count = count;

    /// <summary>
    /// Container offset entries
    /// </summary>
    private readonly Span<Entry> _entries = count == 0 ? []
        : data[(offset + BymlContainer.SIZE)..]
            .ReadSpan<Entry>(count);

    public readonly ImmutableBymlMapEntry this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            Entry entry = _entries[index];
            return new(entry.KeyIndex, _data, entry.Value, entry.Type);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = SIZE)]
    private readonly struct Entry
    {
        public const int SIZE = 8;

        private readonly int _indexAndNodeType;
        public readonly int Value;

        public readonly int KeyIndex {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _indexAndNodeType & 0xFFFFFF;
        }

        public readonly BymlNodeType Type {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BymlNodeType)(_indexAndNodeType >> 24);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator()
        => new(this);

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref struct Enumerator(ImmutableBymlMap container)
    {
        private readonly ImmutableBymlMap _container = container;
        private int _index = -1;

        public readonly ImmutableBymlMapEntry Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _container[_index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (++_index >= _container.Count) {
                return false;
            }

            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SortedDictionary<string, Byml> ToMutable(in ImmutableByml root)
    {
        SortedDictionary<string, Byml> map = [];
        foreach ((var key, var value) in this) {
            map[root.KeyTable[key].ToManaged()]
                = Byml.FromImmutable(value, root);
        }

        return map;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Reverse(ref RevrsReader reader, int offset, int count, in HashSet<int> reversedOffsets)
    {
        for (int i = 0; i < count; i++) {
            reader.Seek(offset + BymlContainer.SIZE + (Entry.SIZE * i));
            reader.Read(3).Reverse();
            BymlNodeType type = reader.Read<BymlNodeType>();
            int value = reader.Read<int>();

            ImmutableByml.ReverseNode(ref reader, value, type, reversedOffsets);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void EmitYaml(YamlEmitter emitter, in ImmutableByml root)
    {
        if (Count <= 5 && !HasContainerNodes()) {
            emitter.Builder.Append('{');
            for (int i = 0; i < Count;) {
                var (keyIndex, node) = this[i];
                Span<byte> key = root.KeyTable[keyIndex];
                emitter.EmitString(key);
                emitter.Builder.Append(": ");
                emitter.EmitNode(node, root);
                if (++i < Count) {
                    emitter.Builder.Append(", ");
                }
            }

            emitter.Builder.Append('}');
            return;
        }

        foreach ((var keyIndex, var node) in this) {
            if (!emitter.IsIndented) {
                emitter.NewLine();
            }

            Span<byte> key = root.KeyTable[keyIndex];
            emitter.IndentLine();
            emitter.EmitString(key);
            emitter.Builder.Append(": ");
            emitter.IsInline = true;
            emitter.IsIndented = false;
            emitter.Level++;
            emitter.EmitNode(node, root);
            emitter.Level--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasContainerNodes()
    {
        foreach ((_, var node) in this) {
            if (node.Type.IsContainerType()) {
                return true;
            }
        }

        return false;
    }
}
