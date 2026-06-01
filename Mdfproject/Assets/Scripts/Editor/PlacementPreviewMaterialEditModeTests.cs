#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PlacementPreviewMaterialEditModeTests
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    [Test]
    public void PlacementPreviewMaterialUsesProjectCompatibleTransparentShader()
    {
        MethodInfo createMethod = typeof(PlacementManager).GetMethod("CreatePreviewMaterial", StaticPrivate);
        Assert.That(createMethod, Is.Not.Null);

        var material = (Material)createMethod.Invoke(null, null);
        try
        {
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.renderQueue, Is.GreaterThanOrEqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
            Assert.That(material.HasProperty("_BaseColor") || material.HasProperty("_Color"), Is.True);

            Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpUnlit != null)
            {
                Assert.That(material.shader, Is.EqualTo(urpUnlit));
            }
        }
        finally
        {
            Object.DestroyImmediate(material);
        }
    }
}
#endif
