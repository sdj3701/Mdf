using System;

/// <summary>
/// Identifies one logical use of a pooled runtime object.  Async work captures a stamp and must
/// re-check it after every await before mutating the object.
/// </summary>
public readonly struct LifecycleStamp : IEquatable<LifecycleStamp>
{
    public LifecycleStamp(int generation, uint identity)
    {
        Generation = generation;
        Identity = identity;
    }

    public int Generation { get; }
    public uint Identity { get; }

    public bool Equals(LifecycleStamp other) =>
        Generation == other.Generation && Identity == other.Identity;

    public override bool Equals(object obj) => obj is LifecycleStamp other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Generation * 397) ^ (int)Identity;
        }
    }
}

/// <summary>
/// Small reusable lifecycle guard shared by pooled Unit/Monster async initialization.
/// </summary>
public sealed class LifecycleGeneration
{
    private int _generation;
    private uint _identity;
    private bool _active;

    public int Generation => _generation;
    public uint Identity => _identity;
    public bool IsActive => _active;

    public LifecycleStamp Begin(uint identity)
    {
        _generation = Next(_generation);
        _identity = identity;
        _active = true;
        return Capture();
    }

    public void End()
    {
        _generation = Next(_generation);
        _identity = 0;
        _active = false;
    }

    public LifecycleStamp Capture() => new LifecycleStamp(_generation, _identity);

    public bool IsCurrent(LifecycleStamp stamp) =>
        _active && stamp.Generation == _generation && stamp.Identity == _identity;

    private static int Next(int value) => value == int.MaxValue ? 1 : value + 1;
}
