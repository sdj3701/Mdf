#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;

public sealed class CombatSchedulerLocalStateEditModeTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void SlotIndexRentsLowestFreeSlotAndKeepsActiveSlotsOrdered()
    {
        Type indexType = typeof(CombatScheduler).Assembly.GetType("CombatSchedulerSlotIndex", true);
        object index = Activator.CreateInstance(indexType, new object[] { 6 });

        Invoke(indexType, index, "BeginRebuild");
        Invoke(indexType, index, "AddFreeFromOrderedRebuild", 0);
        Invoke(indexType, index, "AddActiveFromOrderedRebuild", 1);
        Invoke(indexType, index, "AddFreeFromOrderedRebuild", 2);
        Invoke(indexType, index, "AddFreeFromOrderedRebuild", 3);
        Invoke(indexType, index, "AddActiveFromOrderedRebuild", 4);
        Invoke(indexType, index, "AddFreeFromOrderedRebuild", 5);

        Assert.That(RentLowest(indexType, index), Is.EqualTo(0));
        Assert.That((bool)Invoke(indexType, index, "CommitRentedSlot", 0), Is.True);
        Assert.That(ReadActiveSlots(indexType, index), Is.EqualTo(new[] { 0, 1, 4 }));

        Assert.That((bool)Invoke(indexType, index, "ReleaseActiveSlot", 1), Is.True);
        Assert.That(RentLowest(indexType, index), Is.EqualTo(1), "released lowest slot must be reused first");
        Assert.That((bool)Invoke(indexType, index, "CommitRentedSlot", 1), Is.True);
        Assert.That(ReadActiveSlots(indexType, index), Is.EqualTo(new[] { 0, 1, 4 }));
    }

    [Test]
    public void SlotIndexRejectsDuplicateReleaseWithoutCorruptingFreeList()
    {
        Type indexType = typeof(CombatScheduler).Assembly.GetType("CombatSchedulerSlotIndex", true);
        object index = Activator.CreateInstance(indexType, new object[] { 3 });

        Invoke(indexType, index, "BeginRebuild");
        Invoke(indexType, index, "AddActiveFromOrderedRebuild", 0);
        Invoke(indexType, index, "AddFreeFromOrderedRebuild", 1);
        Invoke(indexType, index, "AddFreeFromOrderedRebuild", 2);

        Assert.That((bool)Invoke(indexType, index, "ReleaseActiveSlot", 0), Is.True);
        Assert.That((bool)Invoke(indexType, index, "ReleaseActiveSlot", 0), Is.False);
        Assert.That(RentLowest(indexType, index), Is.EqualTo(0));
        Assert.That(RentLowest(indexType, index), Is.EqualTo(1));
        Assert.That(RentLowest(indexType, index), Is.EqualTo(2));
        Assert.That(TryRent(indexType, index, out _), Is.False);
    }

    [Test]
    public void SlotMaskTracksSlotsAcrossBothWords()
    {
        Type maskType = typeof(CombatScheduler).Assembly.GetType("CombatSchedulerSlotMask", true);
        object mask = Activator.CreateInstance(maskType);

        Invoke(maskType, mask, "Add", 1);
        Invoke(maskType, mask, "Add", 63);
        Invoke(maskType, mask, "Add", 64);
        Invoke(maskType, mask, "Add", 95);

        Assert.That((bool)Invoke(maskType, mask, "Contains", 1), Is.True);
        Assert.That((bool)Invoke(maskType, mask, "Contains", 64), Is.True);
        Assert.That((int)Invoke(maskType, mask, "Count", 96), Is.EqualTo(4));

        Invoke(maskType, mask, "Remove", 64);
        Assert.That((bool)Invoke(maskType, mask, "Contains", 64), Is.False);
        Assert.That((int)Invoke(maskType, mask, "Count", 96), Is.EqualTo(3));
    }

    private static int[] ReadActiveSlots(Type indexType, object index)
    {
        int count = (int)indexType.GetProperty("ActiveCount", InstanceMembers).GetValue(index);
        var slots = new int[count];
        for (int i = 0; i < count; i++)
        {
            slots[i] = (int)Invoke(indexType, index, "GetActiveSlot", i);
        }

        return slots;
    }

    private static int RentLowest(Type indexType, object index)
    {
        Assert.That(TryRent(indexType, index, out int slot), Is.True);
        return slot;
    }

    private static bool TryRent(Type indexType, object index, out int slot)
    {
        object[] args = { -1 };
        bool result = (bool)Invoke(indexType, index, "TryRentLowest", args);
        slot = (int)args[0];
        return result;
    }

    private static object Invoke(Type type, object instance, string methodName, params object[] args)
    {
        MethodInfo method = type.GetMethod(methodName, InstanceMembers);
        Assert.That(method, Is.Not.Null, $"{type.Name}.{methodName}");
        return method.Invoke(instance, args);
    }
}
#endif
