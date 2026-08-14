// 

using System;
using AutoHitCounter.Utilities;
using static AutoHitCounter.Games.DSR.DSRVersion;

namespace AutoHitCounter.Games.DSR;

public static class DSROffsets
{
    private static DSRVersion? _version;

    public static DSRVersion Version => _version
                                        ?? Version1_0_3_0;

    public static void Initialize(long fileSize, nint moduleBase)
    {
        _version = fileSize switch
        {
            74186240 => Version1_0_1_0,
            75245056 => Version1_0_1_1,
            56756736 => Version1_0_1_2,
            57067008 => Version1_0_3_0,
            50286344 => Version1_0_3_1,
            _ => null
        };

        if (!_version.HasValue)
        {
            MsgBox.Show(
                $@"Unknown patch version (file size: {fileSize}), please report it on GitHub",
                "Unknown patch version");
            return;
        }


        InitializeBaseAddresses(moduleBase);
    }

    public static class WorldChrMan
    {
        public static nint Base;

        public const int PlayerIns = 0x68;

        // WorldChrManImp fields, found via Ghidra 2026-08-14 while mapping the
        // Handle -> EntityId lookup for the poll-based boss-gauge feature below.
        // Confirmed against the live struct definition in the project's Ghidra DB
        // (WorldChrManImp, size 0x1f30): NumLoadedWorldBlockChrs is an int at +0xA0,
        // WorldBlockChr0 is the first slot of an inline array of WorldBlockChr*
        // (8-byte stride) starting at +0xA8.
        public const int NumLoadedWorldBlockChrs = 0xA0;
        public const int WorldBlockChr0 = 0xA8;
    }

    // Per WorldBlockChr instance (one per loaded map block). +0x78 points to an array
    // of {int EntityId; int Handle} pairs (8-byte stride), sorted ascending by
    // EntityId -- this is what WorldBlockChr_GetHandleFromChrId binary-searches for
    // the EntityId -> Handle direction; the boss-gauge feature below does the reverse
    // (Handle -> EntityId) via a linear scan of the same array, since it's sorted by
    // the other field.
    public static class WorldBlockChr
    {
        public const int Count = 0x70;
        public const int Entries = 0x78;

        public const int EntryStride = 0x8;
        public const int EntryEntityId = 0x0;
        public const int EntryHandle = 0x4;
    }

    // MenuMan->BossGauge[0..1], found via Ghidra 2026-08-14. Both slots are a plain
    // static struct (NameId, Handle, MyDamage, NetDamage, one flag byte -- 0x14 bytes
    // per slot) at a fixed offset off the MenuMan global. NameId == -1 means the slot
    // is inactive; a transition away from -1 is a boss gauge appearing. This replaced
    // an earlier native-hook attempt (see git history / project memory) that turned
    // out to target the wrong function (fired on every hit, not boss-specific) --
    // MenuMan is a plain resolvable global and this struct is small and static, so
    // polling it directly is both simpler and safer than hooking here.
    public static class MenuMan
    {
        public static nint Base;

        public const int BossGaugeSlotBase = 0x1050;
        public const int BossGaugeStride = 0x14;
        public const int BossGaugeSlotCount = 2;

        public const int BossGaugeNameId = 0x0;
        public const int BossGaugeHandle = 0x4;
    }

    public static class GameDataMan
    {
        public static nint Base;

        public const int Igt = 0xA4;
    }

    public static nint FallDmgRetAddr;
    public static nint AuxDeathRetAddr;
    public static nint EnvDeathRetAddr;
    
    
    
    public static class Hooks
    {
        public static nint Hit;
        public static nint ApplyHealthDelta;
        public static nint KillChr;
        public static nint CheckAuxAttacker;
        public static nint CheckAuxProc;
        public static nint SetThrowState;
        public static nint ClearThrowState;
        public static nint SetEvent;
        public static nint StartNewGame;
    }

    private static void InitializeBaseAddresses(nint moduleBase)
    {
        WorldChrMan.Base = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x1CEE830,
            Version1_0_1_1 => 0x1C7E820,
            Version1_0_1_2 => 0x1D01FC0,
            Version1_0_3_0 => 0x1D151B0,
            Version1_0_3_1 => 0x1C77E50,
            _ => 0
        };
        
        GameDataMan.Base = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x1D00F50,
            Version1_0_1_1 => 0x1C90F40,
            Version1_0_1_2 => 0x1D146E0,
            Version1_0_3_0 => 0x1D278F0,
            Version1_0_3_1 => 0x1C8A530,
            _ => 0
        };

        // Same values SilkySouls already resolves for this global -- both tools read
        // the same binary, so these are shared, not independently re-derived.
        MenuMan.Base = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x1CFF7C8,
            Version1_0_1_1 => 0x1C8F7B8,
            Version1_0_1_2 => 0x1D12F48,
            Version1_0_3_0 => 0x1D26168,
            Version1_0_3_1 => 0x1C88D98,
            _ => 0
        };


        FallDmgRetAddr= moduleBase+ Version switch
        {
            // WARNING: No match found for: Version1_0_1_0, Version1_0_1_1, Version1_0_1_2
            Version1_0_3_0 => 0x32610E,
            Version1_0_3_1 => 0x3282BE,
            _ => 0
        };
        
        AuxDeathRetAddr = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x31CF9E,
            Version1_0_1_1 => 0x31CC9E,
            Version1_0_1_2 => 0x3201BE,
            Version1_0_3_0 => 0x32D384,
            Version1_0_3_1 => 0x32f594,
            _ => 0
        };
        
        EnvDeathRetAddr = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x1151975,
            Version1_0_1_1 => 0x11517F5,
            Version1_0_1_2 => 0x31A89D,
            Version1_0_3_0 => 0x32084D,
            Version1_0_3_1 => 0x1144085,            //TODO test on 11x patches
            _ => 0
        };

        
        Hooks.Hit = moduleBase + Version switch
        {
            
            Version1_0_1_0 => 0x28065C5,
            Version1_0_1_1 => 0x210131E,
            Version1_0_1_2 => 0x2B90267,
            Version1_0_3_0 => 0x2305994,
            Version1_0_3_1 => 0x395221,
            _ => 0
        };
        
        Hooks.ApplyHealthDelta = moduleBase + Version switch
        {
            // WARNING: No match found for: Version1_0_1_0, Version1_0_1_1, Version1_0_1_2
            Version1_0_3_0 => 0x32071C,
            Version1_0_3_1 => 0x32296C,
            _ => 0
        };
        
        Hooks.KillChr = moduleBase + Version switch
        {
            Version1_0_1_0 => 0xF2E482,
            Version1_0_1_1 => 0x3080D02,
            Version1_0_1_2 => 0x3565DD4,
            Version1_0_3_0 => 0x298DA68,
            Version1_0_3_1 => 0x2653E32,
            _ => 0
        };
        
        Hooks.CheckAuxAttacker = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x2A70883,
            Version1_0_1_1 => 0x34C5E2E,
            Version1_0_1_2 => 0xF60E84,
            Version1_0_3_0 => 0xF87274,
            Version1_0_3_1 => 0x48E8B4,
            _ => 0
        };

        Hooks.CheckAuxProc = moduleBase + Version switch
        {
            // WARNING: No match found for: Version1_0_1_0, Version1_0_1_1, Version1_0_1_2
            Version1_0_3_0 => 0x90D184,
            Version1_0_3_1 => 0x11B08D4,
            _ => 0
        };
        
        Hooks.SetThrowState = moduleBase + Version switch
        {
            Version1_0_1_0 => 0xDE2A81,
            Version1_0_1_1 => 0xCD88A1,
            Version1_0_1_2 => 0x2D830E0,
            Version1_0_3_0 => 0x69B491,
            Version1_0_3_1 => 0x6FFB31,
            _ => 0
        };

        Hooks.ClearThrowState = moduleBase + Version switch
        {
            
            Version1_0_3_0 => 0x275be3e,
            Version1_0_3_1 => 0x46a3ab,
            _ => 0
        };

        
        Hooks.SetEvent = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x4E7490,
            Version1_0_1_1 => 0x4E7240,
            Version1_0_1_2 => 0x4EA770,
            Version1_0_3_0 => 0x4F1470,
            Version1_0_3_1 => 0x4F22B0,
            _ => 0
        };

        Hooks.StartNewGame = moduleBase + Version switch
        {
            Version1_0_1_0 => 0x279259,
            Version1_0_1_1 => 0x278F59,
            Version1_0_1_2 => 0x27C3A9,
            Version1_0_3_0 => 0x2809E9,
            Version1_0_3_1 => 0x282299,
            _ => 0
        };



#if DEBUG
        _baseAddr = moduleBase;
        Console.WriteLine("--- Globals ---");
        PrintOffset("WorldChrMan", WorldChrMan.Base);
        PrintOffset("MenuMan", MenuMan.Base);

        PrintOffset("FallDmgRetAddr", FallDmgRetAddr);
        PrintOffset("AuxDeathRetAddr", AuxDeathRetAddr);
        PrintOffset("EnvDeathRetAddr", EnvDeathRetAddr);

        Console.WriteLine("\n--- Hooks ---");
        PrintOffset("Hit", Hooks.Hit);
        PrintOffset("ApplyHealthDelta", Hooks.ApplyHealthDelta);
        PrintOffset("KillChr", Hooks.KillChr);
        PrintOffset("CheckAuxAttacker", Hooks.CheckAuxAttacker);
        PrintOffset("CheckAuxProc", Hooks.CheckAuxProc);
        PrintOffset("SetThrowState", Hooks.SetThrowState);
        PrintOffset("ClearThrowState", Hooks.ClearThrowState);
        PrintOffset("SetEvent", Hooks.SetEvent);
        PrintOffset("StartNewGame", Hooks.StartNewGame);
      


        Console.WriteLine("\n--- Functions ---");


        Console.WriteLine("\n====================================\n");
#endif
    }

#if DEBUG
    private static nint _baseAddr;
    private static void PrintOffset(string name, nint value)
    {
        var rel = value - _baseAddr;
        Console.WriteLine(rel <= 0
            ? $"  {name,-40} *** NOT SET ***"
            : $"  {name,-40} 0x{(long)value:X}  (0x{(long)rel:X})");
    }
#endif
}

