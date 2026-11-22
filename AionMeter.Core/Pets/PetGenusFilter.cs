namespace AionMeter.Core.Pets;
public static class PetGenusFilter
{
    public static readonly string[] Genera = ["cogni", "fera", "natura", "varian", "special"];
    public static bool Includes(PetDefinition pet, IEnumerable<string> selected)
        => selected.Contains(pet.Genus, StringComparer.OrdinalIgnoreCase);
}
