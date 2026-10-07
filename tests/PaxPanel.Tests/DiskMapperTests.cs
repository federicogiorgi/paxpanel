using PaxPanel.Sensors;

namespace PaxPanel.Tests;

public class DiskMapperTests
{
    [Fact]
    public void Identical_models_map_by_disk_number()
    {
        // This PC: C and E are both "Samsung SSD 990 PRO 2TB"; F, G, O are all "WDC WD4005FZBX".
        var letters = new Dictionary<char, int> { ['C'] = 5, ['E'] = 4, ['F'] = 2, ['G'] = 0, ['O'] = 1, ['D'] = 3 };
        var temps = new Dictionary<int, double> { [5] = 40, [4] = 44, [2] = 39, [0] = 38, [1] = 37, [3] = 43 };
        var byLetter = DiskMapper.TempsByLetter(letters, temps);
        Assert.Equal(40, byLetter['C']);
        Assert.Equal(44, byLetter['E']);
        Assert.Equal(39, byLetter['F']);
        Assert.Equal(38, byLetter['G']);
        Assert.Equal(37, byLetter['O']);
        Assert.Equal(43, byLetter['D']);
    }

    [Fact]
    public void Letters_without_temperature_are_absent()
    {
        var byLetter = DiskMapper.TempsByLetter(new Dictionary<char, int> { ['H'] = 7 }, new Dictionary<int, double>());
        Assert.Empty(byLetter);
    }

    [Fact]
    public void Real_wmi_query_finds_the_system_drive()
    {
        var map = new DiskMapper().LetterToDisk(DateTime.Now);
        Assert.True(map.ContainsKey('C'), "C: should be on a physical disk");
    }
}
