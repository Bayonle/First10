using First10.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Persistence;

/// <summary>
/// Placeholder micro-instruction templates so the pipeline is exercisable before the
/// clinical advisor's library lands (M2 external dependency). All seeded UNAPPROVED:
/// they only ever send under the dev-only Triage:AllowUnapprovedTemplates flag.
/// Content follows the paper's conservative defaults (§R1d): keep yourself safe,
/// don't move victims, pressure on bleeding — behaviour reminders, not treatment.
/// </summary>
public static class TemplateSeeder
{
    public static async Task SeedPlaceholdersAsync(First10DbContext db)
    {
        if (await db.MicroInstructionTemplates.AnyAsync())
        {
            return;
        }

        (string Key, string Language, string Text)[] placeholders =
        [
            ("rta_generic", "english",
             "Help is being arranged. While you wait: keep yourself safe away from traffic. Do NOT move anyone who cannot move themselves. If someone is bleeding, press firmly on the wound with cloth. Do not give food or water."),
            ("rta_generic", "pidgin",
             "We dey arrange help. As you dey wait: stand for safe place, no enter road. NO move anybody wey no fit move by himself. If person dey bleed, press the wound well well with cloth. No give am food or water."),
            ("rta_generic", "yoruba",
             "A ń ṣètò ìrànlọ́wọ́. Bí o ṣe ń dúró: dúró sí ibi àìléwu kúrò ní ojú ọ̀nà. MÁ ṣí ẹnikẹ́ni tí kò lè mira ní ibi kan. Bí ẹnikan bá ń ṣàn ẹ̀jẹ̀, fi aṣọ tẹ̀ ẹ́ mọ́ ojú ọgbẹ́. Má fún un ní oúnjẹ tàbí omi."),

            ("rta_fire", "english",
             "DANGER: stay far from the vehicle — fire and fuel can explode. Move others back at least 50 steps. Do not try to fight the fire. Help is being arranged."),
            ("rta_fire", "pidgin",
             "DANGER: comot far from the motor — fire and fuel fit explode. Tell people make dem shift back like 50 steps. No try quench the fire yourself. We dey arrange help."),
            ("rta_fire", "yoruba",
             "EWU: jìnnà sí ọkọ̀ náà — iná àti epo lè bú gbàù. Jẹ́ kí àwọn ènìyàn padà sẹ́hìn bí ìgbésẹ̀ 50. Má gbìyànjú láti pa iná náà. A ń ṣètò ìrànlọ́wọ́."),

            ("rta_okada", "english",
             "Help is being arranged. Do NOT remove the rider's helmet — it can worsen a neck injury. Do not move the rider unless there is immediate danger like fire. Press firmly on any bleeding with cloth."),
            ("rta_okada", "pidgin",
             "We dey arrange help. NO remove the rider helmet — e fit worsen neck injury. No move the rider unless danger dey like fire. If blood dey comot, press am well with cloth."),
            ("rta_okada", "yoruba",
             "A ń ṣètò ìrànlọ́wọ́. MÁ yọ àṣíborí awakọ̀ náà — ó lè mú ìpalára ọrùn burú sí i. Má ṣí i ní ibi kan àyàfi bí ewu bá wà bí iná. Fi aṣọ tẹ ojú ẹ̀jẹ̀ mọ́lẹ̀."),
        ];

        foreach (var (key, language, text) in placeholders)
        {
            db.MicroInstructionTemplates.Add(new MicroInstructionTemplate
            {
                Id = Guid.NewGuid(),
                Key = key,
                Language = language,
                Text = text,
                Version = 1,
                ApprovedBy = null, // deliberately unapproved — G3 clinical gate
                ApprovedAt = null,
            });
        }
        await db.SaveChangesAsync();
    }
}
