using System.Xml.Linq;
using Slopwatch.Detection;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Regression tests for the XML half of issue #142. SW005 and SW006 have no syntax tree, so their
/// identity comes from <see cref="StructuralPath.ForXmlElement"/>.
/// </summary>
/// <remarks>
/// The namespaced case is the one that matters: every real .csproj carries the MSBuild namespace.
/// Selecting siblings by local name alone matches only no-namespace elements, so a namespaced
/// document collapsed every occurrence to <c>#1of1</c> and two identical elements shared a hash.
/// </remarks>
public class XmlStructuralPathTests
{
    private const string MsBuildNs = "http://schemas.microsoft.com/developer/msbuild/2003";

    [Fact]
    public void NamespacedSiblingsProduceDistinctPaths()
    {
        var doc = XDocument.Parse($"""
            <Project xmlns="{MsBuildNs}">
              <PropertyGroup>
                <NoWarn>CS1591</NoWarn>
                <NoWarn>CS1591</NoWarn>
              </PropertyGroup>
            </Project>
            """);

        var paths = doc.Descendants()
            .Where(e => e.Name.LocalName == "NoWarn")
            .Select(e => StructuralPath.ForXmlElement(e))
            .ToList();

        Assert.Equal(2, paths.Count);
        Assert.Equal(2, paths.Distinct().Count());
    }

    [Fact]
    public void NoNamespaceSiblingsProduceDistinctPaths()
    {
        var doc = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <NoWarn>CS1591</NoWarn>
                <NoWarn>CS1591</NoWarn>
              </PropertyGroup>
            </Project>
            """);

        var paths = doc.Descendants()
            .Where(e => e.Name.LocalName == "NoWarn")
            .Select(e => StructuralPath.ForXmlElement(e))
            .ToList();

        Assert.Equal(2, paths.Count);
        Assert.Equal(2, paths.Distinct().Count());
    }

    [Fact]
    public void SameLocalNameInDifferentNamespacesStaysDistinct()
    {
        // Two elements share a local name but live in different namespaces. Selecting by local
        // name would merge them into one sibling group and give them ordinals from that group.
        var doc = XDocument.Parse($"""
            <Root xmlns:a="urn:a" xmlns:b="urn:b">
              <a:Item>1</a:Item>
              <b:Item>2</b:Item>
            </Root>
            """);

        var paths = doc.Descendants()
            .Where(e => e.Name.LocalName == "Item")
            .Select(e => StructuralPath.ForXmlElement(e))
            .ToList();

        Assert.Equal(2, paths.Count);
        Assert.Equal(2, paths.Distinct().Count());
    }

    [Fact]
    public void SingleElementPathIsStable()
    {
        var doc = XDocument.Parse($"""
            <Project xmlns="{MsBuildNs}">
              <PropertyGroup>
                <NoWarn>CS1591</NoWarn>
              </PropertyGroup>
            </Project>
            """);

        var element = doc.Descendants().Single(e => e.Name.LocalName == "NoWarn");
        var first = StructuralPath.ForXmlElement(element);
        var second = StructuralPath.ForXmlElement(element);

        Assert.Equal(first, second);
    }
}
