// Final Fantasy X Sphere Grid authoritative node-type table
// Apply to dat09.dat (Original), dat10.dat (Standard), or dat11.dat (Expert).
// Array index N corresponds directly to layout-file node index N.

#pragma endian little

enum NodeType : u8 {
    Level3Lock = 0x00,
    Empty = 0x01,
    Strength1 = 0x02, Strength2, Strength3, Strength4,
    Defense1, Defense2, Defense3, Defense4,
    Magic1, Magic2, Magic3, Magic4,
    MagicDefense1, MagicDefense2, MagicDefense3, MagicDefense4,
    Agility1, Agility2, Agility3, Agility4,
    Luck1, Luck2, Luck3, Luck4,
    Evasion1, Evasion2, Evasion3, Evasion4,
    Accuracy1, Accuracy2, Accuracy3, Accuracy4,
    HP200 = 0x22, HP300, MP40, MP20, MP10,
    Level1Lock = 0x27, Level2Lock, Level4Lock,
    DelayAttack = 0x2A, DelayBuster, SleepAttack, SilenceAttack,
    DarkAttack, ZombieAttack, SleepBuster, SilenceBuster, DarkBuster,
    TripleFoul, PowerBreak, MagicBreak, ArmorBreak, MentalBreak, Mug, QuickHit,
    Steal = 0x3A, Use, Flee, Pray, Cheer, Focus, Reflex, Aim, Luck,
    Jinx, Lancet, Guard, Sentinel, SpareChange, Threaten, Provoke,
    Entrust, Copycat, Doublecast, Bribe,
    Cure = 0x4E, Cura, Curaga, NulFrost, NulBlaze, NulShock, NulTide,
    Scan, Esuna, Life, FullLife, Haste, Hastega, Slow, Slowga, Shell,
    Protect, Reflect, Dispel, Regen, Holy, AutoLife,
    Blizzard = 0x64, Fire, Thunder, Water, Fira, Blizzara, Thundara,
    Watera, Firaga, Blizzaga, Thundaga, Waterga, Bio, Demi, Death,
    Drain, Osmose, Flare, Ultima,
    PilferGil = 0x77, FullBreak,
    ExtractPower = 0x79, ExtractMana, ExtractSpeed, ExtractAbility,
    NabGil = 0x7D, QuickPockets,
    HiddenOrNull = 0xFF
};

struct NodeContentHeader {
    u16 format_value;       // Normally 49 (0x31)
    u16 node_count;         // Must agree with the paired layout file
    u16 unknown_04;
    u16 unknown_06;
};

struct SphereGridNodeContent {
    NodeContentHeader header;
    NodeType node_types[header.node_count];
};

SphereGridNodeContent sphere_grid_node_content @ 0x00;
