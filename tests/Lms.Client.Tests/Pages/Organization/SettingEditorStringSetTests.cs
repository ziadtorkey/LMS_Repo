using Bunit;
using Lms.Client.Pages.Organization;
using Lms.Client.Tests.Support;
using Lms.Shared.Configuration;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class SettingEditorStringSetTests : ClientTestContext
{
    private static SettingDefinition Definition => SettingCatalog.All.First(d => d.Name == SettingKeys.StudentsCriticalFields.Name);

    [Fact]
    public async Task StringSet_editor_toggles_values_and_saves_json_array()
    {
        var emitted = new List<string>();
        var cut = Render<SettingEditor>(p => p
            .Add(e => e.Definition, Definition)
            .Add(e => e.Value, "[\"NameAr\",\"NameEn\"]")
            .Add(e => e.ValueChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string>(this, emitted.Add)));

        // Adding a field emits the selected values in catalog order; removing one drops it.
        await cut.Find($"input[data-testid='setting-input-{Definition.Name}-Mobile']").ChangeAsync(new() { Value = true });
        await cut.Find($"input[data-testid='setting-input-{Definition.Name}-NameEn']").ChangeAsync(new() { Value = false });

        Assert.Equal(["[\"NameAr\",\"NameEn\",\"Mobile\"]", "[\"NameAr\"]"], emitted);
    }

    [Fact]
    public void StringSet_editor_labels_the_options_with_student_field_names()
    {
        var cut = Render<SettingEditor>(p => p.Add(e => e.Definition, Definition).Add(e => e.Value, "[]"));

        Assert.Equal(StudentFieldsRequestableCount, cut.FindAll("input[type=checkbox]").Count);
    }

    private static int StudentFieldsRequestableCount => Lms.Shared.People.StudentFields.Requestable.Count;
}
