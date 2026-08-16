using Btd6Localizer;
using Xunit;

namespace Btd6Localizer.Tests;

public class LocXmlTests
{
    [Fact]
    public void Parse_ValidDocument_ReturnsSectionsAndEntriesInOrder()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <LocData>
              <Language>
                <Input>
                  <T id="Click">Click</T>
                  <T id="Tap">Tap</T>
                </Input>
                <TowerNames>
                  <T id="DartMonkey">Dart Monkey</T>
                </TowerNames>
              </Language>
            </LocData>
            """;

        var data = LocXml.Parse(xml);

        Assert.Equal(2, data.Sections.Count);
        Assert.Equal("Input", data.Sections[0].Name);
        Assert.Equal(new[] { "Click", "Tap" }, data.Sections[0].Entries.Select(e => e.Id));
        Assert.Equal("Tap", data.Sections[0].Entries[1].Text);
        Assert.Equal("TowerNames", data.Sections[1].Name);
        Assert.Equal("Dart Monkey", data.Sections[1].Entries[0].Text);
    }

    [Fact]
    public void TryParse_NotXml_ReturnsFalseWithError()
    {
        var ok = LocXml.TryParse("not xml at all", out _, out var error);

        Assert.False(ok);
        Assert.Contains("valid XML", error);
    }

    [Fact]
    public void TryParse_MissingLanguageElement_ReturnsFalseWithError()
    {
        var ok = LocXml.TryParse("<LocData></LocData>", out _, out var error);

        Assert.False(ok);
        Assert.Contains("Language", error);
    }

    [Fact]
    public void TryParse_WrongRootElement_ReturnsFalseWithError()
    {
        var ok = LocXml.TryParse("<NotLocData></NotLocData>", out _, out var error);

        Assert.False(ok);
        Assert.Contains("LocData", error);
    }

    [Fact]
    public void TryParse_MissingIdAttribute_ReturnsFalseWithError()
    {
        const string xml = "<LocData><Language><Input><T>Click</T></Input></Language></LocData>";

        var ok = LocXml.TryParse(xml, out _, out var error);

        Assert.False(ok);
        Assert.Contains("id", error);
    }

    [Fact]
    public void Parse_InvalidDocument_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => LocXml.Parse("<LocData></LocData>"));
    }
}
