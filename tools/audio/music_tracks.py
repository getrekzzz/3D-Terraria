"""The soundtrack: one looping piece per music state (biomes, layers, events, bosses). Degrees are 0-based scale steps
(0 = tonic, 3 = fourth, 4 = fifth, 5 = sixth...). The game maps content music states onto these names (MusicDirector)."""

BASE = {"bars": 16, "density": "medium", "bass": "half", "drums": "none"}


def track(**kw):
    return {**BASE, **kw}


TRACKS = {
    "title": track(bpm=90, root=50, scale="dorian", progression=[0, 3, 0, 3, 5, 4, 0, 4], lead="flute", pad="choir", arp=("bell", 4), bass="whole", density="sparse"),
    "forest_day": track(bpm=110, root=55, scale="major", progression=[0, 4, 5, 3], lead="square", pad="pad", arp=("pluck", 2), bass="walk", drums="light"),
    "forest_night": track(bpm=82, root=57, scale="minor", progression=[0, 5, 2, 6], lead="flute", pad="choir", arp=("bell", 4), bass="whole", density="sparse"),
    "underground": track(bpm=96, root=52, scale="minor", progression=[0, 3, 5, 4], lead="pluck", pad="pad", arp=("triangle", 4), drums="halftime", density="sparse", bright=5000),
    "cavern": track(bpm=90, root=50, scale="dorian", progression=[0, 6, 3, 4], lead="bell", pad="pad", arp=("pluck", 4), bass="whole", density="sparse", bright=4800),
    "desert": track(bpm=100, root=52, scale="phrygian", progression=[0, 1, 0, 6], lead="pulse", arp=("pluck", 2), drums="tribal"),
    "underground_desert": track(bpm=92, root=50, scale="harmonic", progression=[0, 1, 0, 4], lead="flute", pad="pad", arp=("pluck", 4), drums="tribal", density="sparse"),
    "tundra": track(bpm=88, root=60, scale="lydian", progression=[0, 1, 4, 0], lead="bell", pad="choir", arp=("bell", 2), bass="whole", density="sparse", bright=8500),
    "jungle": track(bpm=104, root=53, scale="dorian", progression=[0, 3, 0, 6], lead="flute", arp=("pluck", 2), bass="gallop", drums="tribal"),
    "hive": track(bpm=118, root=55, scale="dorian", progression=[0, 6, 3, 4], lead="pulse", arp=("pluck", 2), bass="pump", drums="rock"),
    "ocean": track(bpm=90, root=58, scale="mixolydian", progression=[0, 6, 3, 0], lead="triangle", pad="pad", arp=("pluck", 4), drums="light", swing=0.3),
    "blight": track(bpm=84, root=49, scale="phrygian", progression=[0, 1, 5, 4], lead="pulse", pad="pad", arp=("pluck", 4), drums="halftime", density="sparse", bright=4200),
    "rot": track(bpm=90, root=51, scale="harmonic", progression=[0, 5, 4, 0], lead="square", pad="choir", arp=("pluck", 4), drums="halftime", density="sparse", bright=4500),
    "radiant": track(bpm=116, root=62, scale="lydian", progression=[0, 1, 0, 4], lead="bell", pad="choir", arp=("triangle", 2), bass="walk", drums="light", bright=9000),
    "mushroom": track(bpm=86, root=57, scale="dorian", progression=[0, 3, 1, 4], lead="bell", pad="pad", arp=("bell", 4), bass="whole", density="sparse"),
    "crypt": track(bpm=100, root=50, scale="harmonic", progression=[0, 5, 3, 4], lead="organ", pad="choir", drums="march"),
    "cinderdeep": track(bpm=120, root=45, scale="phrygian", progression=[0, 1, 0, 6], lead="brass", pad="pad", arp=("pluck", 2), bass="pump", bass_voice="bass_saw", drums="rock", bright=5200),
    "sky": track(bpm=76, root=60, scale="lydian", progression=[0, 4, 1, 0], lead="bell", pad="choir", arp=("bell", 2), bass="whole", density="sparse", bright=9000),
    "temple": track(bpm=98, root=52, scale="phrygian", progression=[0, 1, 6, 0], lead="flute", arp=("pluck", 4), drums="tribal"),
    "eclipse": track(bpm=104, root=48, scale="harmonic", progression=[0, 5, 1, 4], lead="organ", pad="choir", drums="halftime"),
    "blood_moon": track(bpm=96, root=47, scale="minor", progression=[0, 5, 6, 4], lead="brass", pad="choir", arp=("pluck", 4), drums="halftime"),
    "pumpkin_moon": track(bpm=132, root=50, scale="harmonic", progression=[0, 5, 3, 4], lead="pulse", arp=("pluck", 2), bass="gallop", drums="drive", density="busy"),
    "frost_moon": track(bpm=130, root=55, scale="minor", progression=[0, 5, 2, 6], lead="bell", arp=("bell", 2), bass="pump", drums="drive", density="busy"),
    "invasion": track(bpm=138, root=52, scale="minor", progression=[0, 6, 5, 4], lead="square", arp=("pulse", 2), bass="pump", drums="drive", density="busy"),
    # Variants (MusicDirector.Variants.cs): rain over the forest, the town, night versions, underground biome themes, space.
    "rain": track(bpm=78, root=55, scale="dorian", progression=[0, 5, 3, 4], lead="triangle", pad="pad", arp=("bell", 4), bass="whole", density="sparse", bright=4600),
    "town_day": track(bpm=104, root=57, scale="major", progression=[0, 3, 4, 0], lead="flute", pad="pad", arp=("pluck", 2), bass="walk", drums="light", swing=0.25),
    "town_night": track(bpm=80, root=53, scale="mixolydian", progression=[0, 3, 6, 4], lead="bell", pad="choir", arp=("pluck", 4), bass="whole", density="sparse"),
    "jungle_night": track(bpm=92, root=50, scale="dorian", progression=[0, 6, 3, 4], lead="flute", pad="choir", arp=("pluck", 4), bass="whole", drums="tribal", density="sparse"),
    "desert_night": track(bpm=84, root=49, scale="harmonic", progression=[0, 1, 4, 0], lead="flute", pad="pad", arp=("bell", 4), bass="whole", density="sparse"),
    "ocean_night": track(bpm=80, root=55, scale="mixolydian", progression=[0, 6, 3, 0], lead="bell", pad="choir", arp=("pluck", 4), bass="whole", density="sparse", swing=0.3),
    "tundra_night": track(bpm=76, root=57, scale="lydian", progression=[0, 4, 1, 0], lead="bell", pad="choir", arp=("bell", 4), bass="whole", density="sparse", bright=7000),
    "underground_jungle": track(bpm=100, root=48, scale="phrygian", progression=[0, 6, 1, 0], lead="flute", pad="pad", arp=("pluck", 2), bass="gallop", drums="tribal", bright=4800),
    "underground_tundra": track(bpm=86, root=55, scale="lydian", progression=[0, 1, 6, 4], lead="bell", pad="choir", arp=("triangle", 4), bass="whole", density="sparse", bright=6500),
    "underground_blight": track(bpm=92, root=46, scale="phrygian", progression=[0, 1, 6, 5], lead="organ", pad="choir", arp=("pluck", 4), drums="halftime", density="sparse", bright=3800),
    "underground_rot": track(bpm=96, root=47, scale="harmonic", progression=[0, 5, 1, 4], lead="brass", pad="choir", arp=("pluck", 4), drums="halftime", density="sparse", bright=4000),
    "underground_radiant": track(bpm=108, root=60, scale="lydian", progression=[0, 4, 1, 5], lead="bell", pad="choir", arp=("triangle", 2), bass="walk", drums="light", bright=8000),
    "space": track(bpm=70, root=52, scale="lydian", progression=[0, 1, 4, 3], lead="bell", pad="choir", arp=("bell", 4), bass="whole", density="sparse", bright=9500),
    "boss_1": track(bpm=150, root=50, scale="harmonic", progression=[0, 5, 3, 4], lead="square", double="pulse", arp=("pluck", 2), bass="pump",
                    bass_voice="bass_saw", drums="drive", density="busy"),
    "boss_2": track(bpm=156, root=52, scale="phrygian", progression=[0, 1, 6, 4], lead="brass", double="square", arp=("pulse", 2), bass="gallop", drums="drive", density="busy"),
    "boss_3": track(bpm=144, root=45, scale="harmonic", progression=[0, 3, 5, 4], lead="organ", pad="choir", arp=("pluck", 2), bass="pump", drums="drive", density="busy"),
    "boss_queen": track(bpm=140, root=57, scale="dorian", progression=[0, 6, 3, 4], lead="pulse", arp=("bell", 2), bass="gallop", drums="rock", density="busy"),
    "boss_plantera": track(bpm=148, root=53, scale="phrygian", progression=[0, 1, 0, 6], lead="square", pad="pad", arp=("pluck", 2), bass="gallop", drums="drive", density="busy"),
    "boss_lunar": track(bpm=140, root=49, scale="phrygian", progression=[0, 1, 5, 4], lead="bell", pad="choir", arp=("pulse", 2), bass="pump", drums="halftime", density="busy"),
    "boss_moon_lord": track(bpm=160, root=44, scale="harmonic", progression=[0, 1, 5, 4], lead="brass", double="organ", pad="choir", arp=("pulse", 2), bass="pump",
                            bass_voice="bass_saw", drums="drive", density="busy", bars=24),
}
