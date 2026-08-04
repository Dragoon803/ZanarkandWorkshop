// Final Fantasy X Sphere Grid layout
// Apply to dat01.dat (Original), dat02.dat (Standard), or dat03.dat (Expert).
// Verified against Zanarkand Workshop's tested SphereGridParser/SphereGridWriter.

#pragma endian little

enum ClusterVisualType : u16 {
    Single       = 0,
    Small        = 1,
    Medium       = 2,
    Big          = 3,
    SingleAlt    = 4,
    SmallAlt     = 5,
    MediumAlt    = 6,
    BigAlt       = 7
};

enum NodeType : u16 {
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
    HiddenOrNull = 0xFFFF
};

struct SphereGridHeader {
    u16 format_value;       // Normally 49 (0x31)
    u16 cluster_count;
    u16 node_count;
    u16 link_count;
    u16 unknown_08;
    u16 unknown_0A;
    u16 unknown_0C;
    u16 unknown_0E;
};

struct ClusterRecord {
    s16 x;
    s16 y;
    u16 unknown_04;
    ClusterVisualType visual_type;
    u16 unknown_08;
    u16 unknown_0A;
    u16 unknown_0C;
    u16 unknown_0E;
} [[static]];

struct NodeRecord {
    s16 x;
    s16 y;
    u16 unknown_04;
    NodeType redundant_node_type; // dat09/10/11 is authoritative
    u16 cluster_index;
    u16 unknown_0A;
} [[static]];

struct LinkRecord {
    u16 node_a_index;
    u16 node_b_index;
    u16 anchor_node_index; // 0xFFFF = straight; otherwise center of circular arc
    u16 unknown_06;
} [[static]];

struct SphereGridLayout {
    SphereGridHeader header;
    ClusterRecord clusters[header.cluster_count];
    NodeRecord nodes[header.node_count];
    LinkRecord links[header.link_count];
};

SphereGridLayout sphere_grid @ 0x00;
