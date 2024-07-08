# CS2-ByeByeHomelessMod

Every one and a half in-game hours, the mod evicts any homeless household that fails to find a house or shelter (e.g., parks). You can configure the eviction period and method (either letting them move out of the city or simply deleting them) in the settings.

While working on this mod, I discovered that my city of 70,000 residents had 50,000 households stuck finding new homes or in invalid MovingAway state without being properly deleted. No wonder there are so many strange issues in the game. This mod will also delete these abnormal households.

Once these abnormal households are gone, you'll be pleasantly surprised to find that new homeless households, such as those resulting from a building demolition, can generally find new homes or parks, reducing the likelihood of them getting stuck on the streets.

So, I believe that the large number of stuck, undeleted homeless households is the root cause of this bug, which is fixed by this mod.

## How is the mod different from the "disable homeless" toggle in developer mode

The "disable homeless" toggle in developer mode deletes households with the HomelessHousehold component. However, the game is bugged, so many homeless households never acquire this component. It seems only those camping in parks fall under this category, while the more problematic ones stuck on the streets do not. Additionally, there is another bug in the game (omg, why is this game so buggy) causing citizens deleted by this toggle to get stuck at the map's edge. They remain in the game and continue to be counted, which drags down performance.

On the contrary this mod only evicts the bugged homeless stuck on the streets, allowing the game's homeless system to remain active rather than being completely disabled.

## Important Notes

After some testing and tracking of households and their members, I'm fairly confident this mod won't cause save file corruption. However, it's always best to be cautious, so I recommend backing up your save files first.
