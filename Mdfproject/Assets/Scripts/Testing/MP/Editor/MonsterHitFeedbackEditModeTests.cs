#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class MonsterHitFeedbackEditModeTests
{
    [TestCase(100f, 100f, 80f, 100f, true, TestName = "ConfirmedDamage_PlaysFeedback")]
    [TestCase(10f, 100f, 0f, 100f, true, TestName = "LethalDamage_QualifiesForFeedback")]
    [TestCase(0f, 0f, 100f, 100f, false, TestName = "InitialHealthSnapshot_DoesNotPlayFeedback")]
    [TestCase(80f, 100f, 100f, 100f, false, TestName = "Healing_DoesNotPlayFeedback")]
    [TestCase(100f, 100f, 150f, 150f, false, TestName = "MaxHealthBuff_DoesNotPlayFeedback")]
    [TestCase(100f, 100f, 99f, 120f, false, TestName = "MaxHealthChange_DoesNotMasqueradeAsDamage")]
    public void HealthChangeClassifierOnlyAcceptsConfirmedDamage(
        float previousHealth,
        float previousMaxHealth,
        float currentHealth,
        float currentMaxHealth,
        bool expected)
    {
        bool actual = MonsterHitFeedbackPresenter.ShouldPlayForHealthChange(
            previousHealth,
            previousMaxHealth,
            currentHealth,
            currentMaxHealth);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void PresenterNeverUsesMonsterNetworkRootForVisualPunch()
    {
        GameObject root = new GameObject("MonsterHitFeedbackRoot");
        GameObject visual = new GameObject("VisualRoot");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(1.2f, 0.9f, 1.1f);

        try
        {
            MonsterHitFeedbackPresenter presenter = root.AddComponent<MonsterHitFeedbackPresenter>();
            FieldInfo visualRootField = typeof(MonsterHitFeedbackPresenter).GetField(
                "_visualPunchRoot",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(visualRootField, Is.Not.Null);

            presenter.Configure(root.transform);
            Assert.That(visualRootField.GetValue(presenter), Is.Null);

            presenter.Configure(visual.transform);
            Assert.That(visualRootField.GetValue(presenter), Is.SameAs(visual.transform));
            Assert.That(presenter.Play(), Is.True);
            presenter.RestorePresentation();
            Assert.That(visual.transform.localScale, Is.EqualTo(new Vector3(1.2f, 0.9f, 1.1f)));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PresenterIsNotPartOfFusionSimulation()
    {
        Assert.That(typeof(Fusion.NetworkBehaviour).IsAssignableFrom(typeof(MonsterHitFeedbackPresenter)), Is.False);
        Assert.That(MonsterHitFeedbackPresenter.ReactionDurationSeconds, Is.EqualTo(0.08f));
    }
}
#endif
