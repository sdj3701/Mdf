using System.Collections;
using MDF.Runtime.Grid;
using MDF.Runtime.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UguiButton = UnityEngine.UI.Button;
using UguiImage = UnityEngine.UI.Image;

namespace MDF.Tests.PlayMode
{
    public sealed class RuntimePolicyPlayModeTests
    {
        [UnityTest]
        public IEnumerator PooledLifecycleRejectsCompletionFromPreviousUse()
        {
            var lifecycle = new LifecycleGeneration();
            LifecycleStamp firstUse = lifecycle.Begin(101);
            yield return null;

            lifecycle.End();
            LifecycleStamp secondUse = lifecycle.Begin(202);
            yield return null;

            Assert.That(lifecycle.IsCurrent(firstUse), Is.False);
            Assert.That(lifecycle.IsCurrent(secondUse), Is.True);
            Assert.That(lifecycle.Identity, Is.EqualTo(202));
        }

        [UnityTest]
        public IEnumerator LobbyPrewarmRejectsStaleForeignAckAndAllowsCleanRetry()
        {
            const int owner = 4;
            const int firstRevision = 7;
            Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
                true, true, owner, owner + 1, firstRevision, firstRevision,
                LobbyMatchLoadingState.Warming), Is.False, "foreign ACK");
            Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
                true, true, owner, owner, firstRevision - 1, firstRevision,
                LobbyMatchLoadingState.Warming), Is.False, "stale ACK");
            Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
                true, true, owner, owner, firstRevision, firstRevision,
                LobbyMatchLoadingState.Warming), Is.True);

            int retryRevision = LobbyMatchLoadPolicy.NextRevision(new[] { firstRevision, firstRevision });
            yield return null;

            var expected = new[] { 1, 4 };
            var incomplete = new[]
            {
                new LobbyMatchPeerLoadState(1, retryRevision, LobbyMatchLoadingState.Ready),
                new LobbyMatchPeerLoadState(4, retryRevision, LobbyMatchLoadingState.Warming)
            };
            Assert.That(LobbyMatchLoadPolicy.CanLoadScene(retryRevision, expected, incomplete), Is.False);

            var ready = new[]
            {
                new LobbyMatchPeerLoadState(1, retryRevision, LobbyMatchLoadingState.Ready),
                new LobbyMatchPeerLoadState(4, retryRevision, LobbyMatchLoadingState.Ready)
            };
            Assert.That(LobbyMatchLoadPolicy.CanLoadScene(retryRevision, expected, ready), Is.True);
            Assert.That(LobbyMatchLoadPolicy.CanLoadScene(firstRevision, expected, ready), Is.False,
                "a late completion from the canceled attempt must not release the gate");
        }

        [Test]
        public void BossCardRetainsPortraitAtZeroAndDisablesInteraction()
        {
            AttackMonsterCardPolicyState two = AttackMonsterCardPolicy.Resolve(
                new AttackMonsterCardPolicyInput(true, true, 2, 0, 0));
            AttackMonsterCardPolicyState one = AttackMonsterCardPolicy.Resolve(
                new AttackMonsterCardPolicyInput(true, true, 1, 0, 0));
            AttackMonsterCardPolicyState zero = AttackMonsterCardPolicy.Resolve(
                new AttackMonsterCardPolicyInput(true, true, 0, 0, 0));

            Assert.That(two.CountText, Is.EqualTo("x2"));
            Assert.That(one.CountText, Is.EqualTo("x1"));
            Assert.That(zero.CountText, Is.EqualTo("x0"));
            Assert.That(zero.HasPortrait, Is.True);
            Assert.That(zero.IsExhausted, Is.True);
            Assert.That(zero.CanInteract, Is.False);
        }

        [Test]
        public void BossCardViewAppliesResolvedStateWithoutRemovingPortrait()
        {
            var host = new GameObject("boss-card-view-test");
            UguiImage portrait = host.AddComponent<UguiImage>();
            UguiButton cardButton = host.AddComponent<UguiButton>();
            UguiImage background = new GameObject("background").AddComponent<UguiImage>();
            background.transform.SetParent(host.transform, false);
            TMP_Text count = new GameObject("count").AddComponent<TextMeshProUGUI>();
            count.transform.SetParent(host.transform, false);
            AttackMonsterCardView view = host.AddComponent<AttackMonsterCardView>();
            view.Configure(
                portrait,
                count,
                background,
                cardButton,
                Color.white,
                Color.yellow,
                new Color(0.3f, 0.3f, 0.3f, 0.5f));

            Sprite retainedPortrait = Sprite.Create(
                new Texture2D(2, 2),
                new Rect(0, 0, 2, 2),
                Vector2.one * 0.5f);
            portrait.sprite = retainedPortrait;
            AttackMonsterCardPolicyState exhausted = AttackMonsterCardPolicy.Resolve(
                new AttackMonsterCardPolicyInput(true, true, 0, 99, 0));
            view.Apply(exhausted, false);

            Assert.That(count.text, Is.EqualTo("x0"));
            Assert.That(cardButton.interactable, Is.False);
            Assert.That(portrait.sprite, Is.SameAs(retainedPortrait));
            Assert.That(portrait.color.a, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(host.activeSelf, Is.True);

            Object.DestroyImmediate(retainedPortrait.texture);
            Object.DestroyImmediate(retainedPortrait);
            Object.DestroyImmediate(host);
        }

        [UnityTest]
        public IEnumerator WallRemoveControllerRoutesRealButtonThroughFrameDispatchGate()
        {
            var host = new GameObject("wall-action-route-test", typeof(RectTransform));

            var removeObject = new GameObject(
                "remove-button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UguiImage),
                typeof(UguiButton));
            removeObject.transform.SetParent(host.transform, false);
            UguiButton removeButton = removeObject.GetComponent<UguiButton>();
            int requestAttempts = 0;
            bool acceptRequests = true;
            var gate = new FrameDispatchGate();
            using (var route = new FrameGatedButtonRoute(
                       removeButton,
                       gate,
                       () => true,
                       () =>
                       {
                           requestAttempts++;
                           return acceptRequests;
                       }))
            {
                removeButton.onClick.Invoke();
                removeButton.onClick.Invoke();
                Assert.That(requestAttempts, Is.EqualTo(1),
                    "one release path cannot dispatch both uGUI and fallback callbacks in a frame");

                gate.Reset();
                acceptRequests = false;
                removeButton.onClick.Invoke();
                route.TryDispatch(Time.frameCount);
                Assert.That(requestAttempts, Is.EqualTo(2),
                    "a rejected request still retains the same-frame duplicate marker");

                yield return null;
                acceptRequests = true;
                removeButton.onClick.Invoke();
                Assert.That(requestAttempts, Is.EqualTo(3),
                    "a transiently rejected request can be retried on a later frame");
            }

            Object.Destroy(host);
            yield return null;
        }

        [Test]
        public void GridOccupancyIndexOwnsCellMutationWithoutLeakingDictionary()
        {
            var index = new GridOccupancyIndex<object>();
            var cell = new Vector3Int(2, 3, 0);
            var occupant = new object();
            index.Add(cell, occupant);

            Assert.That(index.TryGetValue(cell, out object resolved), Is.True);
            Assert.That(resolved, Is.SameAs(occupant));
            Assert.That(index.Remove(cell), Is.True);
            Assert.That(index.ContainsKey(cell), Is.False);
        }

        [UnityTest]
        public IEnumerator PersonalMapThemeSwitchChangesRenderersWithoutTouchingFieldCollider()
        {
            var root = new GameObject("map-theme-presenter-test");
            GameObject classic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject arena = GameObject.CreatePrimitive(PrimitiveType.Quad);
            classic.transform.SetParent(root.transform, false);
            arena.transform.SetParent(root.transform, false);
            Collider fieldCollider = classic.GetComponent<Collider>();
            Vector3 colliderScale = classic.transform.localScale;

            FieldMapThemePresenter presenter = root.AddComponent<FieldMapThemePresenter>();
            presenter.Configure(
                new[] { classic.GetComponent<Renderer>() },
                new[] { arena.GetComponent<Renderer>() });

            presenter.ApplyTheme((int)MapThemeId.Classic);
            yield return null;
            Assert.That(classic.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(arena.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(fieldCollider.enabled, Is.True);

            presenter.ApplyTheme((int)MapThemeId.Arena);
            yield return null;
            Assert.That(classic.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(arena.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(fieldCollider.enabled, Is.True);
            Assert.That(classic.activeSelf, Is.True);
            Assert.That(classic.transform.localScale, Is.EqualTo(colliderScale));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UiToolkitPrimaryPointerRouteFiltersMouseAndTouchIdentity()
        {
            var host = new GameObject("ui-toolkit-pointer-route-test");
            PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            yield return null;

            var target = new VisualElement { name = "pointer-route-test" };
            target.style.width = 100f;
            target.style.height = 100f;
            document.rootVisualElement.Add(target);
            yield return null;

            int dispatches = 0;
            System.IDisposable route = UiToolkitPrimaryPointerRouter.Register(
                target,
                _ => dispatches++);

            using (PointerUpEvent primaryMouse = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, button = 0 }))
            {
                Assert.That(primaryMouse.isPrimary, Is.True);
                Assert.That(UiToolkitPrimaryPointerRouter.IsPrimary(primaryMouse), Is.True);
                target.SendEvent(primaryMouse);
            }
            yield return null;
            Assert.That(dispatches, Is.EqualTo(1));

            using (PointerUpEvent secondaryMouse = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, button = 1 }))
            {
                Assert.That(secondaryMouse.isPrimary, Is.True,
                    "mouse identity remains primary even when its secondary button is released");
                Assert.That(UiToolkitPrimaryPointerRouter.IsPrimary(secondaryMouse), Is.False);
                target.SendEvent(secondaryMouse);
            }
            Assert.That(dispatches, Is.EqualTo(1));

            using (PointerUpEvent primaryTouch = PointerUpEvent.GetPooled(
                       CreateTouch(0), EventModifiers.None))
            using (PointerUpEvent secondaryTouch = PointerUpEvent.GetPooled(
                       CreateTouch(1), EventModifiers.None))
            {
                Assert.That(primaryTouch.pointerType, Is.EqualTo(UnityEngine.UIElements.PointerType.touch));
                Assert.That(primaryTouch.isPrimary, Is.True);
                Assert.That(UiToolkitPrimaryPointerRouter.IsPrimary(primaryTouch), Is.True);
                target.SendEvent(primaryTouch);

                // Unity 2021 marks an isolated pooled touch as primary because no pointer sequence
                // is active in this test panel. Reproduce the non-primary flag that the panel sets
                // when a second live finger participates in the same sequence.
                SetPointerPrimaryForTest(secondaryTouch, false);
                Assert.That(secondaryTouch.pointerType, Is.EqualTo(UnityEngine.UIElements.PointerType.touch));
                Assert.That(secondaryTouch.isPrimary, Is.False);
                Assert.That(UiToolkitPrimaryPointerRouter.IsPrimary(secondaryTouch), Is.False);
                target.SendEvent(secondaryTouch);
            }
            Assert.That(dispatches, Is.EqualTo(2));

            route.Dispose();
            using (PointerUpEvent pointerUp = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, button = 0 }))
            {
                target.SendEvent(pointerUp);
            }
            Assert.That(dispatches, Is.EqualTo(2));

            Object.Destroy(host);
            Object.Destroy(panelSettings);
        }

        private static Touch CreateTouch(int fingerId)
        {
            object boxedTouch = default(Touch);
            const System.Reflection.BindingFlags fields =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Touch).GetField("m_FingerId", fields)?.SetValue(boxedTouch, fingerId);
            typeof(Touch).GetField("m_Phase", fields)?.SetValue(boxedTouch, TouchPhase.Ended);
            return (Touch)boxedTouch;
        }

        private static void SetPointerPrimaryForTest(PointerUpEvent evt, bool value)
        {
            const System.Reflection.BindingFlags members =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            for (System.Type type = evt.GetType(); type != null; type = type.BaseType)
            {
                System.Reflection.PropertyInfo property = type.GetProperty("isPrimary", members);
                System.Reflection.MethodInfo setter = property?.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(evt, new object[] { value });
                    return;
                }
            }

            Assert.Fail("Unity UI Toolkit no longer exposes a writable isPrimary event property.");
        }
    }

}
