using DDjourneys.Core.Theming;

namespace DDjourneys.Tracking.Tests;

public sealed class MaterialProfileTests
{
	[Theory]
	[InlineData("Bg")]
	[InlineData("Surface")]
	[InlineData("Raised")]
	public void Without_a_material_everything_is_opaque(string role)
	{
		Assert.Equal(255, MaterialProfile.Alpha(MaterialProfile.None, MaterialProfile.Immersive, role, true));
	}

	[Theory]
	[InlineData(MaterialProfile.Mica)]
	[InlineData(MaterialProfile.MicaAlt)]
	[InlineData(MaterialProfile.Acrylic)]
	public void The_page_background_is_always_the_material_itself(string material)
	{
		foreach (string coverage in MaterialProfile.Coverages)
		{
			Assert.Equal(0, MaterialProfile.Alpha(material, coverage, "Bg", false));
			Assert.Equal(0, MaterialProfile.Alpha(material, coverage, "Bg", true));
		}
	}

	[Fact]
	public void Cards_are_solid_on_the_backdrop_level_and_thinner_the_further_it_reaches()
	{
		foreach (bool dark in new[] { false, true })
		{
			byte backdrop = MaterialProfile.Alpha(MaterialProfile.Mica, MaterialProfile.Backdrop, "Surface", dark);
			byte layered = MaterialProfile.Alpha(MaterialProfile.Mica, MaterialProfile.Layered, "Surface", dark);
			byte immersive = MaterialProfile.Alpha(MaterialProfile.Mica, MaterialProfile.Immersive, "Surface", dark);

			Assert.Equal(255, backdrop);
			Assert.True(backdrop > layered && layered > immersive);
		}
	}

	[Fact]
	public void Raised_parts_stay_more_opaque_than_the_cards_they_sit_on()
	{
		foreach (string material in new[] { MaterialProfile.Mica, MaterialProfile.Acrylic })
		{
			foreach (string coverage in new[] { MaterialProfile.Layered, MaterialProfile.Immersive })
			{
				Assert.True(
					MaterialProfile.Alpha(material, coverage, "Raised", true)
					> MaterialProfile.Alpha(material, coverage, "Surface", true));
			}
		}
	}

	[Theory]
	[InlineData(null, MaterialProfile.Mica)]
	[InlineData("MICAALT", MaterialProfile.MicaAlt)]
	[InlineData("glass", MaterialProfile.Mica)]
	public void Material_ids_are_normalized(string? id, string expected)
	{
		Assert.Equal(expected, MaterialProfile.NormalizeMaterial(id));
	}
}
