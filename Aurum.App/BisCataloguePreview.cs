using System.IO;
using System.Text.Json;
using AionMeter.Core.Events;
using AionMeter.Core.Gear;

namespace Aurum;

internal static class BisCataloguePreview
{
    internal static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var gear = GearAdviceCatalog.Load(Path.Combine(data, "gear-advice.json"));
        var arcana = ArcanaAdviceCatalog.Load(Path.Combine(data, "arcana-advice.json"));
        var profiles = JsonSerializer.Deserialize<ArcanaAdviceData>(File.ReadAllText(Path.Combine(data, "arcana-advice.json")))!;
        var reports = new List<object>();
        foreach (var profile in profiles.Classes)
        {
            var gameClass = Enum.Parse<GameClass>(profile.Class);
            foreach (var card in profile.Cards!)
            {
                var slot = Enum.Parse<ArcanaSlot>(card.Slot);
                var names = profile.Goals[card.Slot];
                var panel = new ArcanaPanel(card.Slot + " of " + card.Family, slot, card.Family, card.Stat,
                    names.Take(1).Select(n => new ArcanaSkill(n, 2)).ToArray(), 670);
                var advice = arcana.For(gameClass, panel)!;
                var height = new ArcanaAdviceWindow().RenderPanelPreview(panel, advice, Path.Combine(directory, profile.Class + "-" + slot + ".png"));
                reports.Add(new { profile.Class, Kind = "Arcana", Slot = slot.ToString(), Height = height,
                    FitsAtSetBottom670 = 670 + 10 + height <= 1080 * .92, advice.GoalCard, advice.MainStat, advice.Plan });
            }
            foreach (var slot in Enum.GetValues<GearSlot>().Where(s => s != GearSlot.Unknown))
            {
                var advice = gear.For(gameClass, new("Przykładowy przedmiot · " + slot, slot, true, ["Attack 25"]))!;
                var height = new ItemAdviceWindow().RenderPanelPreview(advice, Path.Combine(directory, profile.Class + "-" + slot + ".png"));
                reports.Add(new { profile.Class, Kind = "Equipment", Slot = slot.ToString(), Height = height, FitsAt1080 = height <= 1080, advice.Build });
            }
        }
        File.WriteAllText(Path.Combine(directory, "catalogue-layout.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
}
