using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace AutoGIS.Civil3D.Proposal.Tests;

public sealed class ProposalPlannerTests
{
    private static readonly ProposalInputs Inputs = new("Test Client", "Test Site", 2026, "Landscape", "TEST-A1");
    private static StandardsManifest Manifest() => StandardsManifest.Parse(StandardsManifestTests.FixtureBytes()).Manifest!;
    private static ProposalPlan Plan() => ProposalPlanner.Build(Inputs, Manifest()).Plan!;

    [Fact]
    public void Final_output_paths_must_fit_windows_extended_path_limits()
    {
        var json = StandardsManifestTests.Fixture();
        json["baseRoots"]![0]!["path"] = "C:/" + string.Join("/", Enumerable.Repeat(new string('a', 249), 131));
        var manifest = StandardsManifestTests.Parse(json).Manifest;
        Assert.NotNull(manifest);
        var result = ProposalPlanner.Build(Inputs, manifest);
        Assert.Null(result.Plan);
        Assert.Contains(result.Issues, i => i.Code == ProposalIssueCodes.UnsafePath);
    }

    [Fact]
    public void Nested_folders_are_created_after_parents_even_when_casing_sorts_children_first()
    {
        var json = StandardsManifestTests.Fixture();
        json["folders"]![2] = "support";
        json["folders"]!.AsArray().Add("Support/Nested");
        json["supportRecords"]![0]!["path"] = "Support/Nested/project.json";
        var result = ProposalPlanner.Build(Inputs, StandardsManifestTests.Parse(json).Manifest!);
        Assert.Empty(result.Issues);
        var complete = new HashSet<string>();
        foreach (var action in result.Plan!.Actions)
        {
            Assert.All(action.Dependencies, d => Assert.Contains(d, complete));
            complete.Add(action.Id);
        }
        Assert.Equal(result.Plan.Actions.Select(a => a.Id).Order(StringComparer.Ordinal), result.Plan.Actions.Select(a => a.Id));
    }

    [Fact]
    public void Unpaired_surrogates_cannot_be_silently_replaced_in_plan_identity()
    {
        foreach (var inputs in new[] { Inputs with { ClientName = "bad\ud800" }, Inputs with { ProjectManager = "bad\udc00" } })
        {
            var result = ProposalPlanner.Build(inputs, Manifest());
            Assert.Null(result.Plan);
            Assert.Contains(result.Issues, i => i.Code == ProposalIssueCodes.InvalidInputs);
        }
        Assert.NotNull(ProposalPlanner.Build(Inputs with { ClientName = "Test \U0001f600" }, Manifest()).Plan);
    }

    [Fact]
    public void Combined_root_component_must_fit_windows_limits()
    {
        var result = ProposalPlanner.Build(Inputs with { ClientName = new string('a', 200), SiteName = new string('b', 100) }, Manifest());
        Assert.Null(result.Plan);
        Assert.Contains(result.Issues, i => i.Code == ProposalIssueCodes.InvalidInputs);
    }

    [Fact]
    public void Same_inputs_produce_identical_plans_without_template_access()
    {
        var manifest = Manifest();
        var first = ProposalPlanner.Build(Inputs, manifest);
        var second = ProposalPlanner.Build(Inputs, manifest);
        Assert.Empty(first.Issues);
        Assert.NotNull(first.Plan);
        Assert.Equal(first.Plan.ToJson(), second.Plan!.ToJson());
        Assert.Equal("C:/AutoGIS-Synthetic/Z_Proposal/2026/Test Client - Test Site", first.Plan.FinalRoot);
        Assert.Equal(new[] { "C:/AutoGIS-Synthetic/Z_Proposal/2026", "Test Client - Test Site" }, first.Plan.FinalRootComponents);
        Assert.Null(first.Plan.Inputs.ClientNumber);
        Assert.Null(first.Plan.Inputs.ProjectNumber);
        Assert.Null(first.Plan.Inputs.ProposalNumber);
        Assert.Null(first.Plan.Inputs.SiteAddress);
        Assert.Null(first.Plan.Inputs.ProjectManager);
    }

    [Fact]
    public void Entire_artifact_inventory_and_direct_overlay_graph_are_planned()
    {
        var plan = Plan();
        Assert.Equal(36, plan.Actions.Length);
        var create = plan.Actions.Where(a => a.Operation is ProposalOperation.CreateFolder or ProposalOperation.CreateModelDrawing
            or ProposalOperation.CreateSheetDrawing or ProposalOperation.CreateSheetSet or ProposalOperation.WriteSupportRecord);
        Assert.Equal(new[] { "", "Model", "Model/Base.dwg", "Model/C-SP Linework.dwg", "Model/Existing Conditions.dwg", "Proposal.dst",
            "Sheets", "Sheets/TEST-01.dwg", "Sheets/TEST-02.dwg", "Sheets/TEST-03.dwg", "Shortcuts", "Support",
            "Support/assumptions.json", "Support/decisions.json", "Support/plan.json", "Support/project.json", "Support/receipt.json", "Support/sources.json" },
            create.Select(a => a.RelativePath).Order(StringComparer.Ordinal));
        Assert.Single(plan.Actions.Where(a => a.Data is FolderData { IsDataShortcutFolder: true }));
        Assert.Equal(ExistingGroundState.Pending, plan.Configuration.ExistingGround);
        Assert.Equal(new[] { "BaseModel", "ExistingConditionsModel", "ProposedDesignModel" },
            plan.Actions.Select(a => a.Data).OfType<ModelDrawingData>().Select(d => d.Role));
        var xrefs = plan.Actions.Where(a => a.Operation == ProposalOperation.AddXref).ToArray();
        Assert.Equal(11, xrefs.Length);
        Assert.Equal(11, plan.Configuration.ExpectedReferences.Length);
        Assert.Equal(new[] { "Base.dwg", "Existing Conditions.dwg" },
            xrefs.Where(a => a.RelativePath == "Model/C-SP Linework.dwg").Select(a => ((XrefData)a.Data).RelativeReferencePath));
        foreach (string sheet in new[] { "Sheets/TEST-01.dwg", "Sheets/TEST-02.dwg", "Sheets/TEST-03.dwg" })
            Assert.Equal(new[] { "../Model/Base.dwg", "../Model/Existing Conditions.dwg", "../Model/C-SP Linework.dwg" },
                xrefs.Where(a => a.RelativePath == sheet).Select(a => ((XrefData)a.Data).RelativeReferencePath));
        Assert.All(xrefs, a =>
        {
            var data = (XrefData)a.Data;
            Assert.Equal("Overlay", data.Reference.Mode);
            Assert.Equal(new Point3(0, 0, 0), data.Reference.Insertion);
            Assert.Equal(1, data.Reference.Scale);
            Assert.Equal(0, data.Reference.Rotation);
        });
        Assert.Equal(Enum.GetValues<SupportContent>().Order(), plan.Actions.Select(a => a.Data).OfType<SupportRecordData>().Select(d => d.Content).Order());
        Assert.Equal(SupportContent.ReceiptAfterVerification, Assert.IsType<SupportRecordData>(plan.Actions[^1].Data).Content);
    }

    [Fact]
    public void Sheet_actions_carry_profile_placeholders_registration_and_property_bindings()
    {
        var plan = Plan();
        var sheets = plan.Actions.Select(a => a.Data).OfType<SheetDrawingData>().ToArray();
        Assert.Equal(3, sheets.Length);
        Assert.Equal(new[] { "TEST-02", "TEST-03", "TEST-01" }, sheets.Select(s => s.Sheet.Number));
        Assert.Equal(new[] { 1, 1, 2 }, sheets.Select(s => s.Profile.Placeholders.Length));
        Assert.All(sheets, s =>
        {
            Assert.Equal("C:/AutoGIS-Synthetic/Templates/Sheet.dwt", s.Profile.Template);
            Assert.Equal("TEST-PAGE", s.Profile.PageSetup);
            Assert.Equal("TEST-TITLE", s.Profile.TitleBlock);
            Assert.Equal("TEST-DEVICE", s.Profile.Plot.Device);
            Assert.Equal(new Rectangle(0, 0, 800, 550), s.Profile.PrintableArea);
            Assert.All(s.Profile.Placeholders, p => Assert.Equal(s.Sheet.Role, p.SheetRole));
        });
        Assert.Equal(3, plan.Actions.Count(a => a.Operation == ProposalOperation.RegisterSheet));
        var bindings = plan.Actions.Select(a => a.Data).OfType<TitleBlockData>().ToArray();
        Assert.Equal(3, bindings.Length);
        Assert.All(bindings, b => { Assert.Equal("Proposal.dst", b.SheetSetPath); Assert.Equal(10, b.Mappings.Length); });
        var metadata = Assert.Single(plan.Actions.Select(a => a.Data).OfType<SheetSetPropertiesData>());
        Assert.Equal(10, metadata.Properties.Length);
        Assert.Equal("2026", Assert.Single(metadata.Properties, p => p.Input == "ProposalYear").Value);
        Assert.Null(Assert.Single(metadata.Properties, p => p.Input == "ClientNumber").Value);
    }

    [Fact]
    public void Ordinal_action_order_is_topological_and_dependencies_cover_artifacts()
    {
        var plan = Plan();
        Assert.Equal(plan.Actions.Select(a => a.Id).Order(StringComparer.Ordinal), plan.Actions.Select(a => a.Id));
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in plan.Actions)
        {
            Assert.All(action.Dependencies, d => Assert.Contains(d, completed));
            Assert.True(completed.Add(action.Id));
            if (action.Operation == ProposalOperation.AddXref)
            {
                var data = (XrefData)action.Data;
                Assert.Contains("20:model/" + data.Reference.ReferenceRole, action.Dependencies);
                Assert.Contains((data.Reference.HostRole.EndsWith("Model", StringComparison.Ordinal) ? "20:model/" : "30:sheet/") + data.Reference.HostRole, action.Dependencies);
            }
            if (action.Operation == ProposalOperation.RegisterSheet)
            {
                Assert.Contains("40:sheet-set", action.Dependencies);
                Assert.Contains("30:sheet/" + ((SheetRegistrationData)action.Data).Sheet.Role, action.Dependencies);
            }
            if (action.Operation == ProposalOperation.BindTitleBlock)
                Assert.Contains("50:properties", action.Dependencies);
            if (action.Operation == ProposalOperation.WriteSupportRecord)
                Assert.Contains("10:folder/00001/Support", action.Dependencies);
        }
        Assert.Equal(plan.Actions.Length - 1, plan.Actions[^1].Dependencies.Length);
    }

    [Fact]
    public void Later_client_number_changes_metadata_and_identity_without_renaming_any_output()
    {
        var manifest = Manifest();
        var first = ProposalPlanner.Build(Inputs, manifest).Plan!;
        var second = ProposalPlanner.Build(Inputs with { ClientNumber = "TEST-CLIENT-1" }, manifest).Plan!;
        Assert.Equal(first.FinalRoot, second.FinalRoot);
        Assert.Equal(first.Actions.Select(a => a.RelativePath), second.Actions.Select(a => a.RelativePath));
        Assert.Equal(first.Actions.Select(a => a.Id), second.Actions.Select(a => a.Id));
        Assert.NotEqual(first.ToJson(), second.ToJson());
        Assert.Equal("TEST-CLIENT-1", second.Configuration.Inputs.ClientNumber);
        var metadata = Assert.Single(second.Actions.Select(a => a.Data).OfType<SheetSetPropertiesData>());
        Assert.Equal("TEST-CLIENT-1", Assert.Single(metadata.Properties, p => p.Input == "ClientNumber").Value);
        Assert.Equal(first.Configuration.Standards.ToJson(), second.Configuration.Standards.ToJson());
    }

    [Fact]
    public void Equivalent_manifest_order_and_current_culture_do_not_change_plan_bytes()
    {
        var json = StandardsManifestTests.Fixture();
        foreach (string key in new[] { "folders", "models", "sheets", "xrefs", "supportRecords", "propertyMappings" })
            json[key] = new JsonArray(json[key]!.AsArray().Reverse().Select(x => x!.DeepClone()).ToArray());
        var reordered = new JsonObject(json.AsObject().Reverse().Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, p.Value!.DeepClone())));
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = ProposalPlanner.Build(Inputs, Manifest()).Plan!;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = ProposalPlanner.Build(Inputs, StandardsManifestTests.Parse(reordered).Manifest!).Plan!;
            Assert.Equal(first.ToJson(), second.ToJson());
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Manifest_changes_bind_configuration_and_plan_identity()
    {
        var json = StandardsManifestTests.Fixture();
        json["profiles"]![0]!["pageSetup"] = "TEST-OTHER-PAGE";
        var plan = ProposalPlanner.Build(Inputs, StandardsManifestTests.Parse(json).Manifest!).Plan!;
        var first = Plan();
        Assert.NotEqual(first.Configuration.Standards.ToJson(), plan.Configuration.Standards.ToJson());
        Assert.NotEqual(first.ToJson(), plan.ToJson());
        Assert.Equal("TEST-OTHER-PAGE", Assert.Single(plan.Configuration.Standards.Profiles).PageSetup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("C:relative")]
    [InlineData("C:/rooted")]
    [InlineData("\\\\server\\share")]
    [InlineData("name/part")]
    [InlineData("name\\part")]
    [InlineData("name:stream")]
    [InlineData("NUL")]
    [InlineData("con.txt")]
    [InlineData("LPT9")]
    [InlineData("COM¹.log")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("name\n")]
    public void Unsafe_names_return_no_plan(string? name)
    {
        foreach (var inputs in new[] { Inputs with { ClientName = name! }, Inputs with { SiteName = name! } })
        {
            var result = ProposalPlanner.Build(inputs, Manifest());
            Assert.Null(result.Plan);
            Assert.Contains(result.Issues, i => i.Code == ProposalIssueCodes.InvalidInputs);
        }
    }

    [Theory]
    [InlineData(0, "Landscape", "TEST-A1", ProposalIssueCodes.InvalidInputs)]
    [InlineData(10000, "Landscape", "TEST-A1", ProposalIssueCodes.InvalidInputs)]
    [InlineData(2027, "Landscape", "TEST-A1", ProposalIssueCodes.UnsupportedYear)]
    [InlineData(2026, "Portrait", "TEST-A1", ProposalIssueCodes.UnsupportedSheet)]
    [InlineData(2026, "Landscape", "TEST-A9", ProposalIssueCodes.UnsupportedSheet)]
    [InlineData(2026, " ", "TEST-A1", ProposalIssueCodes.InvalidInputs)]
    public void Invalid_or_unsupported_selections_fail_closed(int year, string orientation, string size, string code)
    {
        var result = ProposalPlanner.Build(Inputs with { ProposalYear = year, Orientation = orientation, SheetSize = size }, Manifest());
        Assert.Null(result.Plan);
        Assert.Contains(result.Issues, i => i.Code == code);
    }

    [Fact]
    public void Optional_metadata_is_validated_and_all_supplied_values_reach_configuration()
    {
        foreach (var invalid in new[] { Inputs with { ClientNumber = " " }, Inputs with { ProjectNumber = "\n" },
            Inputs with { ProposalNumber = " " }, Inputs with { SiteAddress = "\0" }, Inputs with { ProjectManager = " " } })
            Assert.Null(ProposalPlanner.Build(invalid, Manifest()).Plan);
        var valid = Inputs with { ClientNumber = "C-1", ProjectNumber = "P-2", ProposalNumber = "B-3", SiteAddress = "123 Test St.", ProjectManager = "Test Person" };
        Assert.Equal(valid, ProposalPlanner.Build(valid, Manifest()).Plan!.Configuration.Inputs);
    }

    [Fact]
    public void Approved_plan_collections_cannot_be_mutated_in_place()
    {
        var plan = Plan();
        string before = plan.ToJson();
        Assert.Throws<NotSupportedException>(() => ((IList<PlannedAction>)plan.Actions)[0] = plan.Actions[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)plan.Actions[^1].Dependencies)[0] = "corrupt");
        Assert.Throws<NotSupportedException>(() => ((IList<ModelStandard>)plan.Configuration.Standards.Models)[0] = new("corrupt", "bad"));
        Assert.Throws<NotSupportedException>(() => ((IList<PlaceholderStandard>)plan.Configuration.Standards.Profiles[0].Placeholders).Clear());
        Assert.Equal(before, plan.ToJson());
    }
}
