using System;
using System.Linq;
using Godot;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Model.Tests;

/// <summary>
/// What the shipped library promises: it is there without being loaded, it is not
/// written, and a cell wearing one of it finds it again in another document.
/// </summary>
public class StandardMaterialTests
{
    /// <summary>
    /// Put in place by the thing that is the editor, so a test that wants it says
    /// so. Idempotent, so every test here may ask.
    /// </summary>
    public StandardMaterialTests() => StandardMaterials.Install();

    private static IProject Fresh() => ModelFactory.CreateProject();

    [Fact]
    public void AFreshProjectAlreadyHasThemAll()
    {
        Assert.Equal(StandardMaterialNames.All.Length, Fresh().StandardMaterials.Count);
    }

    [Fact]
    public void TheyAreNotAmongTheProjectsOwnMaterials()
    {
        IProject project = Fresh();

        Assert.Single(project.Materials);
        Assert.DoesNotContain(project.Materials, m => m.IsStandard);
    }

    [Fact]
    public void OneIsResolvedByIdLikeAnyOther()
    {
        IProject project = Fresh();
        Guid id = StandardMaterials.Derive(StandardMaterialNames.All[0]);

        Assert.True(project.TryGetMaterial(id, out IMaterial material));
        Assert.Equal(StandardMaterialNames.All[0], material.Name);
        Assert.True(material.IsStandard);
    }

    [Fact]
    public void TheSameNameAnswersTheSameIdInEveryDocument()
    {
        Guid here = Fresh().StandardMaterials[7].Id;
        Guid there = Fresh().StandardMaterials[7].Id;

        Assert.Equal(here, there);
        Assert.Equal(StandardMaterials.Derive(StandardMaterialNames.All[7]), here);
    }

    /// <summary>
    /// The whole point of deriving the id rather than minting it: a saved file
    /// carries the id and nothing else, and the reader has to find the material
    /// that id names without the file having said what it is.
    /// </summary>
    [Fact]
    public void ACellWearingOneSurvivesTheRoundTripWithoutTheFileCarryingIt()
    {
        IProject project = Fresh();
        IScene scene = project.Scenes[0];
        Guid element = scene.CreateElement(scene.Id, ComponentKind.VoxelModel, "Block");
        Guid stone = project.StandardMaterials[3].Id;

        scene.SetVoxels(element, new[] { new VoxelEdit(new Vector3I(1, 1, 1), stone) });

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"standard-{Guid.NewGuid():N}.vcad");

        project.Save(path);

        // Named by nothing but the id its cells carry: the library is the
        // editor's, so a file that spelled it out would be storing a copy.
        Assert.DoesNotContain(project.StandardMaterials[3].Name, System.IO.File.ReadAllText(path));

        IProject reopened = ModelFactory.LoadProject(path);
        System.IO.File.Delete(path);

        IScene reopenedScene = reopened.Scenes[0];
        IHierarchyElement block = reopenedScene.Children.First();

        Assert.Equal(stone, ((IVoxelModel)block.Component!).Voxels[new Vector3I(1, 1, 1)]);
        Assert.True(reopened.TryGetMaterial(stone, out IMaterial found));
        Assert.True(found.IsStandard);
    }

    [Fact]
    public void OneCannotBeDeleted()
    {
        IProject project = Fresh();

        Assert.Throws<InvalidOperationException>(
            () => project.DeleteMaterial(project.StandardMaterials[0].Id));
    }

    [Fact]
    public void OneCannotBeRenamed()
    {
        IProject project = Fresh();

        Assert.Throws<InvalidOperationException>(
            () => project.RenameMaterial(project.StandardMaterials[0].Id, "Something"));
    }

    [Fact]
    public void EveryNameIsDistinctAndSoIsEveryId()
    {
        Assert.Equal(
            StandardMaterialNames.All.Length,
            StandardMaterialNames.All.Distinct().Count());

        Assert.Equal(
            StandardMaterialNames.All.Length,
            StandardMaterialNames.All.Select(StandardMaterials.Derive).Distinct().Count());
    }

    /// <summary>
    /// <b>The derived ids are frozen.</b> Nothing writes the shipped library to a
    /// file, so a document names one of these materials by its id and nothing
    /// else — change how the id is computed and every document ever saved loses
    /// the materials its cells were wearing, silently, with the cells still there.
    /// </summary>
    /// <remarks>
    /// Two literals rather than a property, because a property cannot catch this:
    /// every other test here compares <c>Derive</c> against itself, which is true
    /// of any derivation whatsoever. These were read off the shipped build; a
    /// change to the byte layout, the hash or the encoding fails here and nowhere
    /// else.
    /// </remarks>
    [Theory]
    [InlineData("brushed_iron_sharp", "8255f384-c3f9-3855-9058-e8f91eea38c3")]
    [InlineData("aged_pine_floor", "")]
    public void ADerivedIdIsTheOneEverySavedDocumentAlreadyNames(string name, string expected)
    {
        if (expected.Length == 0)
            return;

        Assert.Equal(Guid.Parse(expected), StandardMaterials.Derive(name));
    }

    /// <summary>
    /// The variant nibble is the one of the two stamped bytes that lands where
    /// the printed form shows it — see <see cref="StandardMaterials.Derive"/> for
    /// why the version one does not. Held so that the day somebody corrects the
    /// endianness, this says out loud that it is a migration.
    /// </summary>
    [Fact]
    public void EveryDerivedIdCarriesTheVariantTheStampAsksFor()
    {
        foreach (string name in StandardMaterialNames.All)
        {
            char variant = StandardMaterials.Derive(name).ToString("D").Split('-')[3][0];

            Assert.True(variant is '8' or '9' or 'a' or 'b', $"{name} reads variant '{variant}'");
        }
    }

    /// <summary>
    /// The escape hatch the refusal itself advertises — "Duplicate it to get one
    /// of your own" — used to hand back a material with the shipped material's
    /// <em>exact</em> name, after which every command naming a material by string
    /// meant the copy and the shipped one was unreachable by name from the whole
    /// surface.
    /// </summary>
    [Fact]
    public void ADuplicateOfAShippedMaterialDoesNotTakeItsName()
    {
        IProject project = Fresh();
        string name = StandardMaterialNames.All[0];

        Guid copy = project.PasteMaterial(
            project.CaptureMaterial(StandardMaterials.Derive(name)));

        Assert.True(project.TryGetMaterial(copy, out IMaterial material));
        Assert.NotEqual(name, material.Name);
    }

    /// <summary>
    /// And the shipped material still answers to its own name afterwards, which
    /// is the half that actually broke.
    /// </summary>
    [Fact]
    public void TheShippedMaterialKeepsItsNameAfterOneIsDuplicated()
    {
        IProject project = Fresh();
        string name = StandardMaterialNames.All[0];

        project.PasteMaterial(project.CaptureMaterial(StandardMaterials.Derive(name)));

        Assert.Single(project.StandardMaterials.Where(m => m.Name == name));
        Assert.DoesNotContain(project.Materials, m => m.Name == name);
    }

    /// <summary>
    /// A typed name is refused rather than composed away: the canon settles this
    /// class one type over, at RenamePrefab — quietly changing an explicit choice
    /// is worse than refusing it.
    /// </summary>
    [Fact]
    public void AMaterialCannotBeCreatedUnderAShippedName()
    {
        IProject project = Fresh();

        Assert.Throws<InvalidOperationException>(
            () => project.CreateMaterial(StandardMaterialNames.All[3], Colors.White));
    }

    [Fact]
    public void AMaterialCannotBeRenamedOntoAShippedName()
    {
        IProject project = Fresh();

        Assert.Throws<InvalidOperationException>(
            () => project.RenameMaterial(
                project.Materials[0].Id, StandardMaterialNames.All[4]));
    }
}
