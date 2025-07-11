using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UELib.Annotations;
using UELib.Branch;
using UELib.Branch.UE2.AA2;
using UELib.Branch.UE2.CT;
using UELib.Branch.UE2.DNF;
using UELib.Branch.UE2.DVS;
using UELib.Branch.UE2.Eon;
using UELib.Branch.UE2.Lead;
using UELib.Branch.UE2.SCX;
using UELib.Branch.UE2.ShadowStrike;
using UELib.Branch.UE3.APB;
using UELib.Branch.UE3.DD2;
using UELib.Branch.UE3.GIGANTIC;
using UELib.Branch.UE3.HUXLEY;
using UELib.Branch.UE3.MOH;
using UELib.Branch.UE3.R6;
using UELib.Branch.UE3.RL;
using UELib.Branch.UE3.RSS;
using UELib.Branch.UE3.SFX;
using UELib.Branch.UE3.Willow;
using UELib.Branch.UE4;
using UELib.Core;
using UELib.Decoding;
using UELib.Flags;
using UELib.Services;

namespace UELib
{
    public class ObjectEventArgs : EventArgs
    {
        public UObject ObjectRef { get; }

        public ObjectEventArgs(UObject objectRef)
        {
            ObjectRef = objectRef;
        }
    }

    /// <summary>
    /// Represents the method that will handle the UELib.UnrealPackage.NotifyObjectAdded
    /// event of a new added UELib.Core.UObject.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A UELib.UnrealPackage.ObjectEventArgs that contains the event data.</param>
    public delegate void NotifyObjectAddedEventHandler(object sender, ObjectEventArgs e);

    /// <summary>
    /// Represents the method that will handle the UELib.UnrealPackage.NotifyPackageEvent
    /// event of a triggered event within the UELib.UnrealPackage.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A UELib.UnrealPackage.PackageEventArgs that contains the event data.</param>
    public delegate void PackageEventHandler(object sender, UnrealPackage.PackageEventArgs e);

    /// <summary>
    /// Represents the method that will handle the UELib.UnrealPackage.NotifyInitializeUpdate
    /// event of a UELib.Core.UObject update.
    /// </summary>
    public delegate void NotifyUpdateEvent();

    /// <summary>
    /// Registers the class as an Unreal class. The class's name is required to begin with the letter "U".
    /// When an Unreal Package is initializing, all described objects will be initialized as the registered class if its name matches as described by its export item.
    /// 
    /// Note: Usage restricted to the executing assembly(UELib) only!
    /// </summary>
    [MeansImplicitUse]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UnrealRegisterClassAttribute : Attribute
    {
    }

    /// <summary>
    /// Represents data of a loaded unreal package.
    /// </summary>
    public sealed partial class UnrealPackage : IDisposable, IBinaryData
    {
        public const uint Signature = UnrealFile.Signature;
        public const uint Signature_BigEndian = UnrealFile.BigEndianSignature;

        public readonly UPackage RootPackage;

        // Reference to the stream used when reading this package
        public UPackageStream Stream;

        /// <summary>
        /// The full name of this package including directory.
        /// </summary>
        private readonly string _FullPackageName = "UnrealPackage";

        public string FullPackageName => _FullPackageName;
        public string PackageName => Path.GetFileNameWithoutExtension(_FullPackageName);
        public string PackageDirectory => Path.GetDirectoryName(_FullPackageName)!;

        public BinaryMetaData BinaryMetaData { get; } = new();

        // TODO: Move to UnrealBuild.cs
        public sealed class GameBuild : object
        {
            [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
            private sealed class BuildAttribute : Attribute
            {
                private readonly int _MinVersion;
                private readonly int _MaxVersion;
                private readonly uint _MinLicensee;
                private readonly uint _MaxLicensee;

                public readonly BuildGeneration Generation;
                public readonly BuildFlags Flags;

                private readonly bool _VerifyEqual;

                public BuildAttribute(int minVersion, uint minLicensee,
                    BuildGeneration gen = BuildGeneration.Undefined)
                {
                    _MinVersion = minVersion;
                    _MinLicensee = minLicensee;
                    Generation = gen;
                    _VerifyEqual = true;
                }


                public BuildAttribute(int minVersion, uint minLicensee,
                    BuildFlags flags,
                    BuildGeneration gen = BuildGeneration.Undefined)
                {
                    _MinVersion = minVersion;
                    _MinLicensee = minLicensee;
                    Flags = flags;
                    Generation = gen;
                    _VerifyEqual = true;
                }

                public BuildAttribute(int minVersion, int maxVersion, uint minLicensee, uint maxLicensee,
                    BuildGeneration gen = BuildGeneration.Undefined)
                {
                    _MinVersion = minVersion;
                    _MaxVersion = maxVersion;
                    _MinLicensee = minLicensee;
                    _MaxLicensee = maxLicensee;
                    Generation = gen;
                }

                public BuildAttribute(int minVersion, int maxVersion, uint minLicensee, uint maxLicensee,
                    BuildFlags flags,
                    BuildGeneration gen = BuildGeneration.Undefined)
                {
                    _MinVersion = minVersion;
                    _MaxVersion = maxVersion;
                    _MinLicensee = minLicensee;
                    _MaxLicensee = maxLicensee;
                    Flags = flags;
                    Generation = gen;
                }

                public bool Verify(GameBuild gb, PackageFileSummary summary)
                {
                    return _VerifyEqual
                        ? summary.Version == _MinVersion && summary.LicenseeVersion == _MinLicensee
                        : summary.Version >= _MinVersion && summary.Version <= _MaxVersion
                                                         && summary.LicenseeVersion >= _MinLicensee
                                                         && summary.LicenseeVersion <= _MaxLicensee;
                }
            }

            // Note: Some builds use the EngineVersion to represent as the LicenseeVersion, e.g Unreal2 and DCUO.
            public enum BuildName
            {
                Unset,
                Default,
                Unknown,

                /// <summary>
                /// Standard
                /// 
                /// 61/000
                /// </summary>
                [Build(61, 0, BuildGeneration.UE1)] Unreal1,

                /// <summary>
                /// Standard, Unreal Tournament & Deus Ex
                /// 
                /// 68:69/000
                /// </summary>
                [Build(68, 69, 0u, 0u, BuildGeneration.UE1)]
                UT,

                //[Build(80, 0, BuildGeneration.UE1)]
                BrotherBear,

                /// <summary>
                /// Clive Barker's Undying
                ///
                /// 72:85/000 (Only 84 to 85 is auto detected, other versions are overlapping with older unrelated games)
                /// </summary>
                [Build(84, 85, 0u, 0u, BuildGeneration.UE1)]
                Undying,

                /// <summary>
                /// Deus Ex: Invisible War
                /// 
                /// Missing support for custom classes such as BitfieldProperty and BitfieldEnum among others.
                /// 95/69
                /// </summary>
                [Build(95, 69, BuildGeneration.Flesh)] DeusEx_IW,

                /// <summary>
                /// Thief: Deadly Shadows
                /// 
                /// 95/133
                /// </summary>
                [Build(95, 133, BuildGeneration.Flesh)]
                Thief_DS,

                /// <summary>
                /// 99:117/005:008
                /// Latest patch? Same structure as UT2004's UE2.5
                /// 121/029 (Overlapped with UT2004)
                /// </summary>
                [Build(99, 117, 5u, 8u)] UT2003,

                /// <summary>
                /// Tom Clancy's Splinter Cell
                ///
                /// 100/017
                /// </summary>
                [Build(100, 17, BuildGeneration.SCX)] SC,

                /// <summary>
                /// 100/058
                /// </summary>
                [Build(100, 58)] XIII,

                /// <summary>
                /// Tom Clancy's Splinter Cell: Chaos Theory
                ///
                /// 100/120:124
                /// </summary>
                [BuildEngineBranch(typeof(EngineBranchSCX))]
                [Build(100, 022, BuildGeneration.SCX)] // Legacy UWindowFonts.utx
                [Build(100, 099, BuildGeneration.SCX)] // Legacy 00_Training_NPC_TEX_fb.utx
                [Build(100, 120, BuildGeneration.SCX)] // Demo
                [Build(100, 122, BuildGeneration.SCX)] // > Coop_Agents_Dlg.uax
                [Build(100, 124, BuildGeneration.SCX)] // Full
                SCCT_Offline,

                /// <summary>
                /// Tom Clancy's Splinter Cell: Double Agent - Offline
                ///
                /// 100/167
                /// </summary>
                [BuildEngineBranch(typeof(EngineBranchSCX))]
                [Build(100, 167, BuildGeneration.SCX)]
                SCDA_Offline,

                /// <summary>
                /// Tom Clancy's Splinter Cell: Blacklist
                ///
                /// 102/116
                /// </summary>
                [BuildEngineBranch(typeof(EngineBranchLead))]
                [Build(102, 116, BuildGeneration.Lead)]
                SCBL,

                /// <summary>
                /// 110/2609
                /// </summary>
                [Build(110, 110, 2481u, 2609u)] Unreal2,

                /// <summary>
                /// 118:120/004:008
                /// </summary>
                [BuildEngineBranch(typeof(EngineBranchDVS))]
                [Build(118, 120, 4u, 8u)]
                Devastation,

                /// <summary>
                /// Tom Clancy's Rainbow Six 3: Raven Shield
                /// 
                /// 118/011:014
                /// extensions: [.rsm, .u, .uxx, .utx, .uax, .umx, .usx, .ukx, .uvx]
                /// </summary>
                [Build(118, 118, 11u, 14u)] R6RS,

                /// <summary>
                /// Unreal II: eXpanded MultiPlayer
                /// 
                /// 126/000
                /// </summary>
                [Build(123, 126, 0u, 0u)] Unreal2XMP,

                /// <summary>
                /// 118:128/025:029
                /// (Overlaps latest UT2003)
                /// </summary>
                [Build(118, 128, 25u, 29u, BuildGeneration.UE2_5)]
                UT2004,

                /// <summary>
                /// America's Army 2.X
                /// Represents both AAO and AAA
                ///
                /// Built on UT2004
                /// 128/032:033
                /// 
                /// For now we have three AA2 versions defined here to help us distinguish the byte-code token map.
                /// </summary>
                [Build(128, 32u, BuildGeneration.AGP)]
                [BuildEngineBranch(typeof(EngineBranchAA2))]
                AA2_2_5,

                [Build(128, 32u, BuildGeneration.AGP)]
                [BuildEngineBranch(typeof(EngineBranchAA2))]
                AA2_2_6,

                [Build(128, 33u, BuildGeneration.AGP)]
                [BuildEngineBranch(typeof(EngineBranchAA2))]
                AA2_2_8,

                /// <summary>
                /// Vanguard: Saga of Heroes
                /// 
                /// 129/035
                /// Some packages have 128/025 but those are in conflict with UT2004.
                /// </summary>
                [Build(128, 129, 34u, 35u, BuildGeneration.UE2_5)]
                Vanguard_SOH,

                /// <summary>
                /// Shadow Ops: Red Mercury
                ///
                /// 129/010
                /// extensions: [.sfr, .u, .uxx, .utx, .uax, .umx, .usx, .ukx, .uvx]
                /// </summary>
                [Build(129, 010u, BuildGeneration.UE2)]
                RM,

                // IrrationalGames/Vengeance - 129:143/027:059

                /// <summary>
                /// Tribes: Vengeance
                ///
                /// 130/027
                /// </summary>
                [Build(130, 27, BuildGeneration.Vengeance)]
                Tribes_VG,

                /// <summary>
                /// 129/027
                /// </summary>
                [Build(129, 27, BuildGeneration.Vengeance)]
                Swat4,

                /// <summary>
                /// Lemony Snicket's A Series of Unfortunate Events
                /// 
                /// 129/003
                /// </summary>
                [Build(129, 3, BuildGeneration.UE2)] LSGame,

                /// <summary>
                /// Stargate SG-1: The Alliance
                /// 
                /// 130/004
                /// </summary>
                [Build(130, 4, BuildGeneration.UE2_5)] SG1_TA,

                /// <summary>
                /// BioShock 1 & 2
                /// 
                /// 130:143/056:059
                /// </summary>
                [Build(130, 143, 56u, 59u, BuildGeneration.Vengeance)]
                BioShock,

                /// <summary>
                /// Men of Valor
                /// 
                /// 137/000
                /// </summary>
                [Build(137, 0u, BuildGeneration.UE2_5)]
                MOV,

                /// <summary>
                /// Advent Rising
                /// 
                /// 146/61447 (HO: GPlatform, LO: Licensee 007)
                /// </summary>
                [Build(143, 61447u)]
                [Build(145, 61447u)]
                [Build(146, 61447u)]
                [OverridePackageVersion(0, 7)]
                [BuildEngineBranch(typeof(EonEngineBranch))]
                Advent,

                /// <summary>
                /// Duke Nukem Forever
                ///
                /// 156/036
                /// </summary>
                [Build(156, 36u, BuildGeneration.UE2)]
                [BuildEngineBranch(typeof(EngineBranchDNF))]
                DNF,

                /// <summary>
                /// The Chronicles of Spellborn
                /// 
                /// Built on UT2004
                /// 159/029
                /// Comes with several new non-standard UnrealScript features, these are however not supported.
                /// </summary>
                [Build(159, 29u, BuildGeneration.UE2_5)]
                Spellborn,

                /// <summary>
                /// Star Wars: Republic Commando
                ///
                /// 159/01
                /// </summary>
                [Build(134, 01u, BuildGeneration.UE2)]
                [Build(138, 01u, BuildGeneration.UE2)]
                [Build(145, 01u, BuildGeneration.UE2)]
                [Build(148, 01u, BuildGeneration.UE2)]
                [Build(154, 01u, BuildGeneration.UE2)]
                [Build(156, 159, 01u, 01u, BuildGeneration.UE2)]
                [BuildEngineBranch(typeof(CTEngineBranch))]
                SWRepublicCommando,

                /// <summary>
                /// Tom Clancy's Splinter Cell: Chaos Theory - Versus
                ///
                /// 175/000
                /// The 'Epic' version likely represents the internal engine version.
                /// extensions: [.u, .usa, .uxx, .utx, .uax, .umx, .usx, .ukx, .uvx, .sdc]
                /// </summary>
                [Build(175, 000, BuildGeneration.ShadowStrike)] // non dynamic-pc.umd files
                [Build(232, 070, BuildGeneration.ShadowStrike)] // Coop_Char_Voices.uax
                [Build(382, 100, BuildGeneration.ShadowStrike)] // 00_Training_COOP_TEX.utx
                [Build(442, 118, BuildGeneration.ShadowStrike)] // 00_Training_NPC_TEX.utx
                [Build(466, 120, BuildGeneration.ShadowStrike)] // COOP_MAIN_VILAIN_TEX.utx
                [BuildEngineBranch(typeof(EngineBranchShadowStrike))]
                [OverridePackageVersion(120, 175)]
                SCCT_Versus,

                /// <summary>
                /// Tom Clancy's Rainbow Six: Vegas
                ///
                /// 241/066-071
                ///
                /// extensions: [.u, .upk, .uxx, .rmpc, .uppc, .rm3, .up3, .rsm]
                /// </summary>
                [Build(241, 66u)]
                [Build(241, 71u)] // Vegas 2
                [BuildEngineBranch(typeof(EngineBranchKeller))]
                R6Vegas,

                /// <summary>
                /// Tom Clancy's Splinter Cell: Double Agent - Online
                ///
                /// 275/000
                /// Overriden to version 120, so we can pick up the CppText property in UStruct (although this might be a ProcessedText reference)
                /// extensions: [.u, .usa, .uxx, .utx, .uax, .umx, .usx, .ukx, .uvx, .upx, .ute, .uvt, .bsm, .sds]
                /// </summary>
                [Build(249, 0, BuildGeneration.ShadowStrike)] // Content
                [Build(260, 0, BuildGeneration.ShadowStrike)] // Content
                [Build(264, 0, BuildGeneration.ShadowStrike)] // Content
                [Build(272, 0, BuildGeneration.ShadowStrike)] // Content
                [Build(275, 0, BuildGeneration.ShadowStrike)] // Scripts
                [BuildEngineBranch(typeof(EngineBranchShadowStrike))]
                [OverridePackageVersion(120, 275)]
                SCDA_Online,

                /// <summary>
                /// EndWar
                ///
                /// 369/006
                /// </summary>
                [Build(329, 0)]
                [OverridePackageVersion((uint)PackageObjectLegacyVersion.RefactoredPropertyTags)]
                EndWar,

                /// <summary>
                /// Standard
                ///
                /// 369/006
                /// </summary>
                [Build(369, 6)] RoboBlitz,

                /// <summary>
                /// Stranglehold
                ///
                /// 375/025
                /// </summary>
                [Build(375, 25, BuildGeneration.Midway3)]
                Stranglehold,

                /// <summary>
                /// Medal of Honor: Airborne
                ///
                /// 421/011
                /// </summary>
                [Build(421, 11)] MoHA,

                /// <summary>
                /// Frontlines: Fuel of War
                ///
                /// 433/052
                /// </summary>
                [Build(433, 52)] FFoW,

                /// <summary>
                /// 472/046
                /// </summary>
                [Build(472, 46, BuildFlags.ConsoleCooked)]
                MKKE,

                /// <summary>
                /// Stargate Worlds
                ///
                /// 486/007
                /// </summary>
                [OverridePackageVersion((uint)PackageObjectLegacyVersion.PackageFlagsAddedToExports - 1)] // Missing the "PackageFlags" change from version 475
                [Build(486, 7)] SGW,

                /// <summary>
                /// Gears of War
                ///
                /// 490/009
                /// </summary>
                [Build(490, 9)] GoW1,

                /// <summary>
                /// Huxley
                ///
                /// 496/016:023
                /// </summary>
                [Build(496, 496, 16, 23)]
                [BuildEngineBranch(typeof(EngineBranchHuxley))]
                Huxley,

                [Build(511, 039, BuildGeneration.HMS)] // The Bourne Conspiracy
                [Build(511, 145, BuildGeneration.HMS)] // Transformers: War for Cybertron (PC version)
                [Build(511, 144, BuildGeneration.HMS)]
                // Transformers: War for Cybertron (PS3 and XBox 360 version)
                Transformers,

                /// <summary>
                /// 512/000
                /// </summary>
                [Build(512, 0)] UT3,

                /// <summary>
                /// 536/043
                /// </summary>
                [Build(536, 43)] MirrorsEdge,

                /// <summary>
                /// Transformers: Dark of the Moon
                /// </summary>
                [Build(537, 174, BuildGeneration.HMS)] Transformers2,

                /// <summary>
                /// 539/091
                /// </summary>
                [Build(539, 91)] AlphaProtocol,

                /// <summary>
                /// APB: All Points Bulletin & APB: Reloaded
                ///
                /// 547/028:032
                /// </summary>
                [Build(547, 547, 28u, 32u)]
                [BuildEngineBranch(typeof(EngineBranchAPB))]
                APB,

                /// <summary>
                /// Standard, Gears of War 2
                ///
                /// 575/000
                /// Xenon is enabled here, because the package is missing editor data, the editor data of UStruct is however still serialized.
                /// </summary>
                [Build(575, 0, BuildFlags.XenonCooked)]
                GoW2,

                /// <summary>
                /// 576/005
                /// </summary>
                [Build(576, 5)] CrimeCraft,

                /// <summary>
                /// Batman: Arkham Asylum
                ///
                /// 576/021 (Missing most changes guarded by <see cref="BuildGeneration.RSS"/>)
                /// </summary>
                [Build(576, 21, BuildGeneration.RSS)]
                [BuildEngineBranch(typeof(EngineBranchRSS))]
                Batman1,

                /// <summary>
                /// 576/100
                /// </summary>
                [Build(576, 100)] Homefront,

                /// <summary>
                /// Medal of Honor (2010)
                /// Windows, PS3, Xbox 360
                /// Defaulting to ConsoleCooked.
                /// XenonCooked is required to read the Xbox 360 packages.
                /// 581/058
                /// </summary>
                [Build(581, 58, BuildFlags.ConsoleCooked)]
                [BuildEngineBranch(typeof(EngineBranchMOH))]
                MoH,

                /// <summary>
                /// Borderlands
                ///
                /// 584/057-058
                ///
                /// Includes back-ported features from UDK
                /// </summary>
                [Build(584, 584, 57, 58, BuildGeneration.GB)]
                [BuildEngineBranch(typeof(EngineBranchWillow))]
                Borderlands,

                /// <summary>
                /// Borderlands Game of the Year Enhanced
                ///
                /// 594/058
                ///
                /// Includes back-ported features from UDK of at least v813 (NativeClassGroup)
                /// Appears to be missing (v623:ExportGuids, v767:TextureAllocations, and v673:FPropertyTag's BoolValue change).
                /// Presume at least v832 from Borderlands 2
                /// </summary>
                [Build(594, 58, BuildGeneration.GB)]
                [OverridePackageVersion(832)]
                [BuildEngineBranch(typeof(EngineBranchWillow))]
                Borderlands_GOTYE,

                /// <summary>
                /// 584/126
                /// </summary>
                [Build(584, 126)] Singularity,

                /// <summary>
                /// 590/001
                /// </summary>
                [Build(590, 1, BuildFlags.XenonCooked)]
                ShadowComplex,

                /// <summary>
                /// The Exiled Realm of Arborea
                ///
                /// 610/014
                /// </summary>
                [Build(610, 14)] Tera,

                /// <summary>
                /// DC Universe Online
                ///
                /// 648/6405
                /// </summary>
                [Build(648, 6405)] DCUO,

                /// <summary>
                /// Mass Effect: Legendary Edition
                ///
                /// 684/171
                /// Engine: 6383
                /// Cooker: 65643
                /// </summary>
                [Build(391, 0092, BuildGeneration.SFX)] // Xenon
                [Build(491, 1008, BuildGeneration.SFX)] // PC
                [Build(684, 0153, BuildFlags.ConsoleCooked, BuildGeneration.SFX)] // PS3
                [Build(684, 0171, BuildFlags.ConsoleCooked, BuildGeneration.SFX)] // LE
                [BuildEngineBranch(typeof(EngineBranchSFX))]
                ME1,

                [Build(512, 0130, BuildGeneration.SFX)] // Demo
                [Build(513, 0130, BuildGeneration.SFX)] // PC
                [Build(684, 0150, BuildFlags.ConsoleCooked, BuildGeneration.SFX)] // PS3
                [Build(684, 0168, BuildGeneration.SFX)] // LE
                [BuildEngineBranch(typeof(EngineBranchSFX))]
                ME2,

                [Build(684, 0185, BuildGeneration.SFX)] // Demo
                [Build(684, 0194, BuildFlags.ConsoleCooked, BuildGeneration.SFX)] // PC
                [Build(845, 0194, BuildFlags.ConsoleCooked, BuildGeneration.SFX)] // Wii
                [Build(685, 0205, BuildGeneration.SFX)] // LE
                [BuildEngineBranch(typeof(EngineBranchSFX))]
                ME3,

                /// <summary>
                /// Dungeon Defenders 2
                ///
                /// 687-688/111-117
                /// </summary>
                [Build(687, 688, 111, 117)]
                [BuildEngineBranch(typeof(EngineBranchDD2))]
                DD2,

                /// <summary>
                /// BioShock Infinite
                /// 727/075 (partially upgraded to 756 or higher)
                /// </summary>
                [Build(727, 75)]
                [OverridePackageVersion((uint)PackageObjectLegacyVersion.SuperReferenceMovedToUStruct)]
                Bioshock_Infinite,

                /// <summary>
                /// Bulletstorm
                ///
                /// 742/029
                /// </summary>
                [Build(742, 29)]
                Bulletstorm,

                /// <summary>
                /// 801/030
                /// </summary>
                [Build(801, 30)] Dishonored,

                /// <summary>
                /// 788/001,828/000
                /// </summary>
                [Build(788, 1, BuildFlags.ConsoleCooked)]
                // Conflict with GoW3
                //[Build(828, 0, BuildFlags.ConsoleCooked)]
                InfinityBlade,

                /// <summary>
                /// Standard, Gears of War 3
                ///
                /// 828/000
                /// </summary>
                [Build(828, 0, BuildFlags.ConsoleCooked)]
                GoW3,

                /// <summary>
                /// 832/021
                /// </summary>
                [Build(832, 21)] RememberMe,

                /// <summary>
                /// Borderlands 2
                ///
                /// 832/046, 895/046
                /// </summary>
                [Build(832, 46)]
                [Build(895, 46)] // VR
                [BuildEngineBranch(typeof(EngineBranchWillow))]
                Borderlands2,

                /// <summary>
                /// Gears of War: Ultimate Edition / Reloaded
                ///
                /// 835/56, 835/76
                /// </summary>
                [Build(835, 56)] // Ultimate Edition
                [Build(835, 76)] // Reloaded
                [OverridePackageVersion(490, 10)]
                GoWUE,

                /// <summary>
                /// 842-864/001
                /// </summary>
                [Build(842, 1, BuildFlags.ConsoleCooked)]
                [Build(864, 1, BuildFlags.ConsoleCooked)]
                InfinityBlade2,

                // Cannot auto-detect, ambiguous with UDK-2015-01-29
                //[Build(868, 0, BuildFlags.ConsoleCooked)]
                InfinityBlade3,

                /// <summary>
                /// 868/008
                /// </summary>
                [Build(868, 8, BuildFlags.ConsoleCooked)]
                InjusticeMobile,

                /// <summary>
                /// XCom
                ///
                /// 845/059
                /// </summary>
                [Build(845, 59)] XCOM_EU,

                /// <summary>
                /// XCom 2: War of The Chosen
                ///
                /// 845/120
                /// </summary>
                [Build(845, 120)] XCOM2WotC,

                /// <summary>
                /// Transformers: Fall of Cybertron
                /// 846/181
                /// </summary>
                [Build(846, 181, BuildGeneration.HMS)]
                [OverridePackageVersion(587)]
                Transformers3,

                /// <summary>
                /// 860/002-004
                /// </summary>
                [Build(860, 860, 2, 4)] Hawken,

                /// <summary>
                /// Batman: Arkham City
                ///
                /// 805/101
                /// </summary>
                [Build(805, 101, BuildGeneration.RSS)]
                [BuildEngineBranch(typeof(EngineBranchRSS))]
                Batman2,

                /// <summary>
                /// Batman: Arkham Origins
                ///
                /// 806/103
                /// 807/137-138
                /// </summary>
                [Build(806, 103, BuildGeneration.RSS)]
                [Build(807, 807, 137, 138, BuildGeneration.RSS)]
                [BuildEngineBranch(typeof(EngineBranchRSS))]
                Batman3,

                /// <summary>
                /// 807/104
                /// </summary>
                [Build(807, 104, BuildGeneration.RSS)]
                [BuildEngineBranch(typeof(EngineBranchRSS))]
                Batman3MP,

                /// <summary>
                /// Batman: Arkham Knight
                ///
                /// 863/32995(227 & ~8000)
                /// </summary>
                [Build(863, 32995, BuildGeneration.RSS)]
                [OverridePackageVersion(863, 227)]
                [BuildEngineBranch(typeof(EngineBranchRSS))]
                Batman4,

                /// <summary>
                /// Gigantic: Rampage Edition
                ///
                /// 867/008:010
                /// </summary>
                [Build(867, 867, 8u, 10u)]
                [BuildEngineBranch(typeof(EngineBranchGigantic))]
                Gigantic,

                /// <summary>
                /// Rocket League
                ///
                /// 867/009:032
                /// Requires third-party decompression and decryption
                /// </summary>
                [Build(867, 868, 9u, 32u)]
                [BuildEngineBranch(typeof(EngineBranchRL))]
                RocketLeague,

                /// <summary>
                /// Battleborn
                ///
                /// 874/078
                ///
                /// EngineVersion and CookerVersion are packed with the respective Licensee version.
                /// </summary>
                [BuildEngineBranch(typeof(EngineBranchWillow))]
                [Build(874, 78u)] Battleborn,

                /// <summary>
                /// A Hat in Time
                ///
                /// 877:893/005
                ///
                /// The earliest available version with any custom specifiers is 1.0 (877) - Un-Drew.
                /// </summary>
                [Build(877, 893, 5, 5)] AHIT,

                /// <summary>
                /// Bulletstorm: Full Clip Edition
                ///
                /// 8887/041
                /// </summary>
                [Build(887, 41)]
                Bulletstorm_FCE,

                /// <summary>
                /// Special Force 2
                ///
                /// 904/009 (Non-standard version, actual Epic version might be 692 or higher)
                /// </summary>
                [Build(904, 904, 09u, 014u)]
                [OverridePackageVersion((uint)PackageObjectLegacyVersion.ProbeMaskReducedAndIgnoreMaskRemoved)]
                SpecialForce2,
            }

            public BuildName Name { get; }

            [Obsolete] public uint Version { get; }

            [Obsolete] public uint LicenseeVersion { get; }

            public uint? OverrideVersion { get; }
            public ushort? OverrideLicenseeVersion { get; }

            public BuildGeneration Generation { get; internal set; }
            public readonly Type? EngineBranchType;

            [Obsolete("To be deprecated")] public readonly BuildFlags Flags;

            public GameBuild(uint overrideVersion, ushort overrideLicenseeVersion, BuildGeneration generation,
                Type engineBranchType,
                BuildFlags flags)
            {
                OverrideVersion = overrideVersion;
                OverrideLicenseeVersion = overrideLicenseeVersion;
                Generation = generation;
                EngineBranchType = engineBranchType;
                Flags = flags;
            }

            public GameBuild(UnrealPackage package)
            {
                // If UE Explorer's PlatformMenuItem is equal to "Console", set ConsoleCooked flag.
                // This is required for correct serialization of unrecognized Console packages
                if (UnrealConfig.Platform == UnrealConfig.CookedPlatform.Console)
                {
                    Flags |= BuildFlags.ConsoleCooked;
                }

                var buildInfo = FindBuildInfo(package, out var buildAttribute);
                if (buildInfo == null)
                {
                    Name = package.Summary.LicenseeVersion == 0
                        ? BuildName.Default
                        : BuildName.Unknown;
                    return;
                }

                Name = (BuildName)Enum.Parse(typeof(BuildName), buildInfo.Name);
                Version = package.Summary.Version;
                LicenseeVersion = package.Summary.LicenseeVersion;

                if (buildAttribute != null)
                {
                    Generation = buildAttribute.Generation;
                    Flags = buildAttribute.Flags;
                }

                var overrideAttribute = buildInfo.GetCustomAttribute<OverridePackageVersionAttribute>(false);
                if (overrideAttribute != null)
                {
                    if (overrideAttribute.FixedVersion != 0) OverrideVersion = overrideAttribute.FixedVersion;
                    if (overrideAttribute.FixedLicenseeVersion != 0) OverrideLicenseeVersion = overrideAttribute.FixedLicenseeVersion;
                }

                var engineBranchAttribute = buildInfo.GetCustomAttribute<BuildEngineBranchAttribute>(false);
                if (engineBranchAttribute != null)
                {
                    // We cannot create the instance here, because the instance itself may be dependent on GameBuild.
                    EngineBranchType = engineBranchAttribute.EngineBranchType;
                }
            }

            private FieldInfo? FindBuildInfo(UnrealPackage linker, out BuildAttribute? buildAttribute)
            {
                buildAttribute = null;

                // Auto-detect
                if (linker.BuildTarget == BuildName.Unset)
                {
                    var builds = typeof(BuildName).GetFields();
                    foreach (var build in builds)
                    {
                        var buildAttributes = build.GetCustomAttributes<BuildAttribute>(false);
                        buildAttribute = buildAttributes.FirstOrDefault(attr => attr.Verify(this, linker.Summary));
                        if (buildAttribute == null)
                            continue;

                        return build;
                    }

                    return null;
                }

                if (linker.BuildTarget != BuildName.Unknown)
                {
                    string buildName = Enum.GetName(typeof(BuildName), linker.BuildTarget);
                    var build = typeof(BuildName).GetField(buildName);
                    return build;
                }

                return null;
            }

            public static bool operator ==(GameBuild b, BuildGeneration gen)
            {
                return b.Generation == gen;
            }

            public static bool operator !=(GameBuild b, BuildGeneration gen)
            {
                return b.Generation != gen;
            }

            public static bool operator ==(GameBuild b, BuildName name)
            {
                return b.Name == name;
            }

            public static bool operator !=(GameBuild b, BuildName name)
            {
                return b.Name != name;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Name == (BuildName)obj;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return (int)Name;
            }

            public override string ToString()
            {
                return Name.ToString();
            }
        }

        public GameBuild.BuildName BuildTarget = GameBuild.BuildName.Unset;

        /// <summary>
        /// The auto-detected (can be set before deserialization to override auto-detection).
        /// This needs to be set to the correct build in order to load some game-specific data from packages.
        /// </summary>
        public GameBuild Build;

        /// <summary>
        /// The branch that we are using to load the data contained within this package.
        /// </summary>
        public EngineBranch Branch;

        /// <summary>
        /// The platform that the cooker was cooking this package for.
        /// Needs to be set to Console for decompressed .xxx packages etc.
        /// </summary>
        public BuildPlatform CookerPlatform;

        public struct PackageFileSummary : IUnrealSerializableClass
        {
            public uint Tag;
            public uint Version;
            public ushort LicenseeVersion;

            /// <summary>
            /// Legacy file version if the package was serialized with UE4 or UE5.
            ///
            /// 0 if the file was serialized with UE3 or earlier.
            /// </summary>
            public int LegacyVersion;

            public uint UE4Version;
            public uint UE4LicenseeVersion;

            public UnrealFlags<PackageFlag> PackageFlags;

            /// <summary>
            /// The size of the package header, including the tables.
            /// </summary>
            public int HeaderSize;

            /// <summary>
            /// UPK content category e.g. Weapons, Sounds or Meshes.
            /// </summary>
            public string FolderName;

            public string LocalizationId;

            public int NameCount, NameOffset;
            public int ExportCount, ExportOffset;
            public int ImportCount, ImportOffset;
            public int HeritageCount, HeritageOffset;

            /// <summary>
            /// Table of package guids. early UE1 way of defining generations.
            ///
            /// Null if (<see cref="Version"/> &gt;= <see cref="PackageObjectLegacyVersion.HeritageTableDeprecated"/>)
            /// </summary>
            public UArray<UGuid> Heritages;

            public int DependsOffset;

            public UGuid Guid;

            /// <summary>
            /// Table of package generations.
            ///
            /// Null if (<see cref="Version"/> &lt; <see cref="PackageObjectLegacyVersion.HeritageTableDeprecated"/>)
            /// </summary>
            public UArray<UGenerationTableItem> Generations;

            private PackageFileEngineVersion PackageEngineVersion;
            private PackageFileEngineVersion PackageCompatibleEngineVersion;

            /// <summary>
            /// Displaced with <see cref="PackageObjectLegacyVersion.AddedCookerVersion"/>
            /// </summary>
            [Obsolete] public const int VCookerVersion = 277;

            public int EngineVersion;
            public int CookerVersion;

            public uint CompressionFlags;

            /// <summary>
            /// A list of compressed chunks in the package.
            /// The package should be considered compressed if any.
            ///
            /// If <see cref="CompressionFlags"/> equals 0 then the list will be cleared on <see cref="UnrealPackage.Deserialize"/>
            ///
            /// Null if (<see cref="Version"/> &lt; <see cref="PackageObjectLegacyVersion.CompressionAdded"/>)
            /// </summary>
            public UArray<CompressedChunk> CompressedChunks;

            public uint PackageSource;

            /// <summary>
            /// Null if (<see cref="Version"/> &lt; <see cref="PackageObjectLegacyVersion.AddedAdditionalPackagesToCook"/>)
            /// </summary>
            public UArray<string> AdditionalPackagesToCook;

            public int ImportExportGuidsOffset;
            public int ImportGuidsCount;
            public int ExportGuidsCount;

            public int ThumbnailTableOffset;

            /// <summary>
            /// Null if (<see cref="Version"/> &lt; <see cref="PackageObjectLegacyVersion.AddedTextureAllocations"/>)
            /// </summary>
            public UArray<PackageTextureType> TextureAllocations;

            public int GatherableTextDataCount;
            public int GatherableTextDataOffset;

            public int StringAssetReferencesCount;
            public int StringAssetReferencesOffset;

            public int SearchableNamesOffset;

            public UGuid PersistentGuid;
            public UGuid OwnerPersistentGuid;

            public int AssetRegistryDataOffset;
            public int BulkDataOffset;
            public int WorldTileInfoDataOffset;

            public UArray<int> ChunkIdentifiers;

            public int PreloadDependencyCount;
            public int PreloadDependencyOffset;

            private void SetupBuild(UnrealPackage package)
            {
                // Auto-detect
                if (package.Build == null)
                {
                    package.Build = new GameBuild(package);

                    if (package.Build.Flags.HasFlag(BuildFlags.ConsoleCooked))
                    {
                        package.CookerPlatform = BuildPlatform.Console;
                    }
                }

                if (package.CookerPlatform == BuildPlatform.Undetermined)
                {
                    string packageFolderName = new DirectoryInfo(package.PackageDirectory).Name;
                    if (string.Compare(
                            packageFolderName,
                            "CookedPC",
                            StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        package.CookerPlatform = BuildPlatform.PC;
                    }
                    // file may also end in .pcc
                    else if (string.Compare(
                                 packageFolderName,
                                 "CookedPCConsole",
                                 StringComparison.OrdinalIgnoreCase
                             ) == 0)
                    {
                        package.CookerPlatform = BuildPlatform.PC;
                    }
                    else if (string.Compare(
                                 packageFolderName,
                                 "CookedPCServer",
                                 StringComparison.OrdinalIgnoreCase
                             ) == 0)
                    {
                        package.CookerPlatform = BuildPlatform.Console;
                    }
                    else if (string.Compare(
                                 packageFolderName,
                                 "CookedXenon",
                                 StringComparison.OrdinalIgnoreCase
                             ) == 0)
                    {
                        package.CookerPlatform = BuildPlatform.Console;
                    }
                    else if (string.Compare(
                                 packageFolderName,
                                 "CookedIPhone",
                                 StringComparison.OrdinalIgnoreCase
                             ) == 0)
                    {
                        package.CookerPlatform = BuildPlatform.Console;
                    }
                    else if (Path.GetExtension(package.FullPackageName) == ".xxx")
                    {
                        // ... fully compressed
                    }
                }

                if (package.Build.OverrideVersion.HasValue) Version = package.Build.OverrideVersion.Value;
                if (package.Build.OverrideLicenseeVersion.HasValue)
                    LicenseeVersion = package.Build.OverrideLicenseeVersion.Value;

                if (OverrideVersion != 0) Version = OverrideVersion;
                if (OverrideLicenseeVersion != 0) LicenseeVersion = OverrideLicenseeVersion;
            }

            // TODO: Re-use an instantiated branch if available and if the package's version and licensee are an identical match.
            private void SetupBranch(UnrealPackage package)
            {
                if (package.Build.EngineBranchType != null)
                {
                    package.Branch = (EngineBranch)Activator.CreateInstance(package.Build.EngineBranchType,
                        package.Build.Generation);

                    // The branch may override the generation. (Especially in unit-tests this is useful)
                    package.Build.Generation = package.Branch.Generation;
                }
                else if (package.Summary.UE4Version > 0)
                {
                    package.Branch = new EngineBranchUE4();
                }
                else
                {
                    package.Branch = new DefaultEngineBranch(package.Build.Generation);
                }

                package.Branch.Setup(package);
            }

            public void Serialize(IUnrealStream stream)
            {
                if (stream.Position == 0)
                {
                    stream.Write(Tag);
                }

                if (LegacyVersion < 0)
                {
#if UE4
                    stream.Write(LegacyVersion);
                    if (LegacyVersion != -4)
                    {
                        stream.Write(Version);
                    }

                    stream.Write(UE4Version);
                    stream.Write(UE4LicenseeVersion);

                    if (UE4Version >= 138 && UE4Version < 142)
                    {
                        stream.Write(0);
                        stream.Write(0);
                    }

                    if (LegacyVersion <= -2)
                    {
                        throw new NotSupportedException("This version of the Unreal Engine 4 is not supported!");
                    }
#else
                    throw new NotSupportedException("Unreal Engine 4+ package files are not supported!");
#endif
                }
                else
                {
                    uint version = Version | (uint)(LicenseeVersion << 16);
                    stream.Write(version);
                }

#if SPLINTERCELLX
                if (stream.Package.Build == BuildGeneration.SCX &&
                    stream.LicenseeVersion >= 83)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    stream.Skip(4);
                }
#endif
#if BIOSHOCK
                if (stream.Package.Build == GameBuild.BuildName.Bioshock_Infinite)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    stream.Skip(4);
                }
#endif
#if MKKE
                if (stream.Package.Build == GameBuild.BuildName.MKKE)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    stream.Skip(8);
                }
#endif
#if TRANSFORMERS
                if (stream.Package.Build == BuildGeneration.HMS &&
                    stream.LicenseeVersion >= 55)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    if (stream.LicenseeVersion >= 181) stream.Skip(16);

                    stream.Skip(4);
                }
#endif
#if HUXLEY
                if (stream.Package.Build == GameBuild.BuildName.Huxley)
                {
                    if (LicenseeVersion >= 8)
                    {
                        stream.Write(0xFEFEFEFE);
                    }

                    if (LicenseeVersion >= 17)
                    {
                        throw new NotSupportedException("This package version is not supported!");

                        stream.Skip(4);
                    }
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedTotalHeaderSize)
                {
                    stream.Write(HeaderSize);
                }
#if MIDWAY
                if (stream.Package.Build == BuildGeneration.Midway3 &&
                    stream.LicenseeVersion >= 2)
                {
                    throw new NotSupportedException("This package version is not supported!");
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedFolderName)
                {
                    if (FolderName == string.Empty)
                    {
                        FolderName = "None";
                    }

                    stream.Write(FolderName);
                }

                // version >= 34
                stream.Write((uint)PackageFlags);

#if HAWKEN || GIGANTIC
                if ((stream.Package.Build == GameBuild.BuildName.Hawken ||
                     stream.Package.Build == GameBuild.BuildName.Gigantic) &&
                    stream.LicenseeVersion >= 2)
                {
                    throw new NotSupportedException("This package version is not supported!");
                }
#endif
#if MASS_EFFECT
                if (stream.Package.Build == BuildGeneration.SFX)
                {
                    // Untested, but seen in the reverse-engineered assembly...
                    if ((int)PackageFlags < 0)
                    {
                        // ... virtual call (didn't reverse)
                    }

                    if (PackageFlags.HasFlag(PackageFlag.Cooked) &&
                        stream.LicenseeVersion >= 194 &&
                        stream.LicenseeVersion != 1008)
                    {
                        // SFXPatch Version (according to a localized string that references the same global constant)
                        throw new NotSupportedException("This package version is not supported!");
                    }
                }
#endif
                stream.Write(NameCount);
                stream.Write(NameOffset);
#if UE4
                if (stream.UE4Version >= 516 && stream.Package.ContainsEditorData())
                {
                    stream.Write(LocalizationId);
                }

                if (stream.UE4Version >= 459)
                {
                    stream.Write(GatherableTextDataCount);
                    stream.Write(GatherableTextDataOffset);
                }
#endif
                stream.Write(ExportCount);
                stream.Write(ExportOffset);
#if APB
                if (stream.Package.Build == GameBuild.BuildName.APB &&
                    stream.LicenseeVersion >= 28)
                {
                    if (stream.LicenseeVersion >= 29)
                    {
                    }

                    throw new NotSupportedException("This package version is not supported!");
                }
#endif
                stream.Write(ImportCount);
                stream.Write(ImportOffset);

                if (stream.Version < (uint)PackageObjectLegacyVersion.HeritageTableDeprecated)
                {
                    if (Heritages == null || Heritages.Count == 0)
                    {
                        HeritageCount = 1;
                        Heritages = new UArray<UGuid>(HeritageCount) { Guid };
                    }

                    stream.Write(HeritageCount);
                    stream.Write(HeritageOffset);

                    return;
                }
#if MIDWAY
                if (stream.Package.Build == GameBuild.BuildName.Stranglehold &&
                    stream.Version >= 375)
                {
                    throw new NotSupportedException("This package version is not supported!");
                    //stream.Read(out int _);
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedDependsTable)
                {
                    stream.Write(DependsOffset);
                }
#if THIEF_DS || DEUSEX_IW
                if (stream.Package.Build == GameBuild.BuildName.Thief_DS ||
                    stream.Package.Build == GameBuild.BuildName.DeusEx_IW)
                {
                    throw new NotSupportedException("This package version is not supported!");
                    //int unknown = stream.ReadInt32();
                }
#endif
#if SPLINTERCELLX
                if (stream.Package.Build == BuildGeneration.SCX &&
                    stream.LicenseeVersion >= 12)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    // compiled-constant: SC1: 0xff0adde, SC3: DE AD F0 0F
                    //stream.Read(out int uStack_10c);

                    // An FString converted to an FArray? Concatenating appUserName, appComputerName, appBaseDir, and appTimestamp.
                    //stream.ReadArray(out UArray<byte> iStack_fc);
                }
#endif
#if R6
                if (stream.Package.Build == GameBuild.BuildName.R6Vegas)
                {
                    if (stream.LicenseeVersion >= 48)
                    {
                        // always zero
                        stream.Write(0);
                    }

                    if (stream.LicenseeVersion >= 49)
                    {
                        // it appears next to the LicenseeVersion, so it's probably an internal version
                        // always 14, but 15 for V2, probably the cooker version.
                        stream.Write(CookerVersion);
                    }
                }
#endif
                if (stream.UE4Version >= 384)
                {
                    stream.Write(StringAssetReferencesCount);
                    stream.Write(StringAssetReferencesOffset);
                }

                if (stream.UE4Version >= 510)
                {
                    stream.Write(SearchableNamesOffset);
                }

                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedImportExportGuidsTable &&
                    stream.UE4Version == 0
                    // FIXME: Correct the output version of these games instead.
#if BIOSHOCK
                    && stream.Package.Build != GameBuild.BuildName.Bioshock_Infinite
#endif
#if BORDERLANDS
                    && stream.Package.Build != GameBuild.BuildName.Borderlands_GOTYE
#endif
                   )
                {
                    stream.Write(ImportExportGuidsOffset);
                    stream.Write(ImportGuidsCount);
                    stream.Write(ExportGuidsCount);
                }
#if TRANSFORMERS
                if (stream.Package.Build == BuildGeneration.HMS &&
                    stream.Version >= 535)
                {
                    // FIXME: unverified
                    stream.Write(ThumbnailTableOffset);

                    return;
                }
#endif
#if DD2
                // No version check found in the .exe
                if (stream.Package.Build == GameBuild.BuildName.DD2 && PackageFlags.HasFlag(PackageFlag.Cooked))
                {
                    throw new NotSupportedException("This package version is not supported!");
                }
#endif
#if TERA
                if (stream.Package.Build == GameBuild.BuildName.Tera)
                {
                    goto skipThumbnailTableOffset;
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedThumbnailTable)
                {
                    stream.Write(ThumbnailTableOffset);
                }
            skipThumbnailTableOffset:
#if SPELLBORN
                if (stream.Package.Build == GameBuild.BuildName.Spellborn
                    && stream.Version >= 148)
                {
                    goto skipGuid;
                }
#endif
                stream.WriteStruct(ref Guid);
            skipGuid:
#if MKKE
                if (stream.Package.Build == GameBuild.BuildName.MKKE)
                {
                    goto skipGenerations;
                }
#endif
#if UE4
                if (stream.Package.ContainsEditorData())
                {
                    if (stream.UE4Version >= 518)
                    {
                        stream.Write(ref PersistentGuid);
                        if (stream.UE4Version < 520)
                        {
                            stream.Write(ref OwnerPersistentGuid);
                        }
                    }
                }
#endif
                if (Generations == null || Generations.Count == 0)
                {
                    Generations = new UArray<UGenerationTableItem>(1)
                    {
                        new() { ExportCount = ExportCount, NameCount = NameCount, NetObjectCount = 0 }
                    };
                }

                stream.Write(Generations.Count);
#if APB
                // Guid, however only serialized for the first generation item.
                if (stream.Package.Build == GameBuild.BuildName.APB &&
                    stream.LicenseeVersion >= 32)
                {
                    throw new NotSupportedException("This package version is not supported!");
                    //stream.Skip(16);
                }
#endif
                foreach (var element in Generations)
                {
                    element.Serialize(stream);
                }

            skipGenerations:
#if DNF
                if (stream.Package.Build == GameBuild.BuildName.DNF &&
                    stream.Version >= 151)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    if (PackageFlags.HasFlags(0x20U))
                    {
                        //int buildMonth = stream.ReadInt32();
                        //int buildYear = stream.ReadInt32();
                        //int buildDay = stream.ReadInt32();
                        //int buildSeconds = stream.ReadInt32();
                    }

                    //string dnfString = stream.ReadString();

                    // DLC package
                    if (PackageFlags.HasFlags(0x80U))
                    {
                        // No additional data, just DLC authentication.
                    }
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedEngineVersion &&
                    stream.UE4Version == 0)
                {
                    stream.Write(EngineVersion);
                }
#if UE4
                if (stream.UE4Version >= 336)
                {
                    stream.Write(ref PackageEngineVersion);
                }

                if (stream.UE4Version >= 444)
                {
                    stream.Write(ref PackageCompatibleEngineVersion);
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedCookerVersion &&
                    stream.UE4Version == 0)
                {
                    stream.Write(CookerVersion);
                }
#if MASS_EFFECT
                if (stream.Package.Build == BuildGeneration.SFX)
                {
                    throw new NotSupportedException("This package version is not supported!");

                    // Appears to be similar to a PackageFileEngineVersion

                    if (stream.LicenseeVersion >= 16 && stream.LicenseeVersion < 136)
                    {
                        //stream.Read(out int _);
                    }

                    if (stream.LicenseeVersion >= 32 && stream.LicenseeVersion < 136)
                    {
                        //stream.Read(out int _);
                    }

                    if (stream.LicenseeVersion >= 35 && stream.LicenseeVersion < 113)
                    {
                        //stream.ReadMap(out UMap<string, UArray<string>> branch);
                    }

                    if (stream.LicenseeVersion >= 37)
                    {
                        // Compiler-Constant ? 1
                        //stream.Read(out int _);

                        // Compiler-Constant changelist? 1376256 (Mass Effect 1: LE)
                        //stream.Read(out int _);
                    }

                    if (stream.LicenseeVersion >= 39 && stream.LicenseeVersion < 136)
                    {
                        //stream.Read(out int _);
                    }
                }
#endif
                // Read compressed info?
                if (stream.Version >= (uint)PackageObjectLegacyVersion.CompressionAdded)
                {
                    stream.Write(CompressionFlags);
                    stream.WriteArray(CompressedChunks);
                }
                else
                {
                    // When serializing to an older pkg format...
                    CompressionFlags = 0;
                    CompressedChunks = null;
                }

                // SFX reads 392?
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedPackageSource)
                {
                    stream.Write(PackageSource);
                }
#if MASS_EFFECT
                if (stream.Package.Build == BuildGeneration.SFX)
                {
                    if (stream.LicenseeVersion >= 44 && stream.LicenseeVersion < 136)
                    {
                        throw new NotSupportedException("This package version is not supported!");
                        //stream.Read(out int _);
                    }
                }
#endif
#if UE4
                if (stream.UE4Version > 0)
                {
                    return;
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedAdditionalPackagesToCook)
                {
#if TRANSFORMERS
                    if (stream.Package.Build == BuildGeneration.HMS)
                    {
                        return;
                    }
#endif
                    stream.WriteArray(AdditionalPackagesToCook);
                }
#if BORDERLANDS
                if (stream.Package.Build == GameBuild.BuildName.Borderlands_GOTYE)
                {
                    return;
                }
#endif
#if BATTLEBORN
                if (stream.Package.Build == GameBuild.BuildName.Battleborn)
                {
                    // FIXME: Package format is being deserialized incorrectly and fails here.
                    throw new NotSupportedException("This package version is not supported!");
                    //stream.ReadUInt32();

                    return;
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedTextureAllocations)
                {
                    stream.WriteArray(TextureAllocations);
                }
#if ROCKETLEAGUE
                if (stream.Package.Build == GameBuild.BuildName.RocketLeague
                    && PackageFlags.HasFlag(PackageFlag.Cooked))
                {
                    throw new NotSupportedException("This package version is not supported!");

                    //int garbageSize = stream.ReadInt32();
                    //Debug.WriteLine(garbageSize, "GarbageSize");
                    //int compressedChunkInfoOffset = stream.ReadInt32();
                    //Debug.WriteLine(compressedChunkInfoOffset, "CompressedChunkInfoOffset");
                    //int lastBlockSize = stream.ReadInt32();
                    //Debug.WriteLine(lastBlockSize, "LastBlockSize");
                    //Debug.Assert(stream.Position == NameOffset, "There is more data before the NameTable");
                    //// Data after this is encrypted
                }
#endif
#if UE4
                if (stream.UE4Version >= 112)
                {
                    stream.Write(AssetRegistryDataOffset);
                }

                if (stream.UE4Version >= 212)
                {
                    stream.Write(BulkDataOffset);
                }

                if (stream.UE4Version >= 224)
                {
                    stream.Write(WorldTileInfoDataOffset);
                }

                if (stream.UE4Version >= 278)
                {
                    ChunkIdentifiers ??= [];

                    if (stream.UE4Version >= 326)
                    {
                        stream.Write(ChunkIdentifiers);
                    }
                    else
                    {
                        stream.Write(ChunkIdentifiers.Count > 0 ? ChunkIdentifiers[0] : 0);
                    }
                }

                if (stream.UE4Version >= 507)
                {
                    stream.Write(PreloadDependencyCount);
                    stream.Write(PreloadDependencyOffset);
                }
#endif
            }

            public void Deserialize(IUnrealStream stream)
            {
                if (stream.Position == 0)
                {
                    stream.Read(out Tag);
                }

                const short maxLegacyVersion = -7;

                // Read as one variable due Big Endian Encoding.
                int legacyVersion = stream.ReadInt32();
                // FIXME: >= -7 is true for the game Quantum
                if (legacyVersion < 0 && legacyVersion >= maxLegacyVersion)
                {
                    LegacyVersion = legacyVersion;
#if UE4
                    uint ue3Version = 0;
                    if (legacyVersion != -4)
                    {
                        ue3Version = stream.ReadUInt32();
                    }

                    UE4Version = stream.ReadUInt32();
                    UE4LicenseeVersion = stream.ReadUInt32();

                    Version = ue3Version;

                    // Ancient, probably no longer in production files? Other than some UE4 assets found in the first public release
                    if (UE4Version >= 138 && UE4Version < 142)
                    {
                        stream.Skip(8); // CookedVersion, CookedLicenseeVersion
                    }

                    if (legacyVersion <= -2)
                    {
                        // Read enum based version
                        if (legacyVersion == -2)
                        {
                            int count = stream.ReadInt32(); // Versions
                            stream.Skip(count * (4 + 4)); // Tag, Version
                        }
                        else if (legacyVersion >= -5)
                        {
                            int count = stream.ReadInt32();
                            for (var i = 0; i < count; ++i)
                            {
                                stream.ReadStruct(out UGuid key);
                                stream.Read(out int version);
                                stream.Read(out string friendlyName);
                            }
                        }
                        else
                        {
                            int count = stream.ReadInt32();
                            for (var i = 0; i < count; ++i)
                            {
                                stream.ReadStruct(out UGuid key);
                                stream.Read(out int version);
                            }
                        }
                    }
#else
                    throw new NotSupportedException("This version of the Unreal Engine 4 is not supported!");
#endif
                }
                else
                {
                    Version = (uint)legacyVersion;
                }

                LicenseeVersion = (ushort)(Version >> 16);
                Version &= 0xFFFFU;
                Console.WriteLine("Package Version:" + Version + "/" + LicenseeVersion);

                Contract.Assert(Version != 0, "Bad package version 0!");

                SetupBuild(stream.Package);
                Debug.Assert(stream.Package.Build != null);
                Console.WriteLine("Build:" + stream.Package.Build);

                SetupBranch(stream.Package);
                Debug.Assert(stream.Package.Branch != null);
                Console.WriteLine("Branch:" + stream.Package.Branch);
#if SPLINTERCELLX
                // Starting with SC3
                if (stream.Package.Build == BuildGeneration.SCX &&
                    stream.LicenseeVersion >= 83)
                {
                    stream.Read(out int v08);
                }
#endif
#if R6
                if (stream.Package.Build == GameBuild.BuildName.R6Vegas)
                {
                    if (stream.LicenseeVersion >= 48)
                    {
                        // always zero
                        stream.Read(out int v08);
                    }

                    if (stream.LicenseeVersion >= 49)
                    {
                        // it appears next to the LicenseeVersion, so it's probably an internal version
                        // always 14, but 15 for V2
                        stream.Read(out int v0C);

                        // Let's assume it's the cooker version (it has the same offset as GoW 2006, which has cooker version 32)
                        CookerVersion = v0C;
                    }
                }
#endif
#if LEAD
                if (stream.Package.Build == BuildGeneration.Lead)
                {
                    stream.Read(out int v08);
                }
#endif
#if BIOSHOCK
                if (stream.Package.Build == GameBuild.BuildName.Bioshock_Infinite)
                {
                    int unk = stream.ReadInt32();
                }
#endif
#if MKKE
                if (stream.Package.Build == GameBuild.BuildName.MKKE) stream.Skip(8);
#endif
#if TRANSFORMERS
                if (stream.Package.Build == BuildGeneration.HMS &&
                    stream.LicenseeVersion >= 55)
                {
                    if (stream.LicenseeVersion >= 181) stream.Skip(16);

                    stream.Skip(4);
                }
#endif
#if HUXLEY
                if (stream.Package.Build == GameBuild.BuildName.Huxley)
                {
                    if (LicenseeVersion >= 8)
                    {
                        uint huxleySignature = stream.ReadUInt32();
                        Contract.Assert(huxleySignature == 0xFEFEFEFE, "[HUXLEY] Invalid Signature!");
                    }

                    if (LicenseeVersion >= 17)
                    {
                        int unk = stream.ReadInt32();
                    }
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedTotalHeaderSize)
                {
                    // Offset to the first class(not object) in the package.
                    HeaderSize = stream.ReadInt32();
                    Console.WriteLine("Header Size: " + HeaderSize);
                }
#if MIDWAY
                if (stream.Package.Build == BuildGeneration.Midway3 &&
                    stream.LicenseeVersion >= 2)
                {
                    stream.Read(out int abbrev);

                    string codename = Encoding.UTF8.GetString(BitConverter.GetBytes(abbrev));
                    Console.WriteLine($"Midway game codename: {codename}");

                    stream.Read(out int customVersion);

                    if (customVersion >= 256)
                    {
                        stream.Read(out int _);
                    }
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedFolderName)
                {
                    FolderName = stream.ReadString();
                }

                // version >= 34
                PackageFlags = stream.ReadFlags32<PackageFlag>();
                Console.WriteLine("Package Flags:" + PackageFlags);
#if HAWKEN || GIGANTIC
                if ((stream.Package.Build == GameBuild.BuildName.Hawken ||
                     stream.Package.Build == GameBuild.BuildName.Gigantic) &&
                    stream.LicenseeVersion >= 2)
                {
                    stream.Read(out int vUnknown);
                }
#endif
#if MASS_EFFECT
                if (stream.Package.Build == BuildGeneration.SFX)
                {
                    // Untested, but seen in the reverse-engineered assembly...
                    if ((int)PackageFlags < 0)
                    {
                        // ... virtual call (didn't reverse)
                    }

                    if (PackageFlags.HasFlag(PackageFlag.Cooked) &&
                        stream.LicenseeVersion >= 194 &&
                        stream.LicenseeVersion != 1008)
                    {
                        // SFXPatch Version (according to a localized string that references the same global constant)
                        int v94 = stream.ReadInt32();
                    }
                }
#endif
                NameCount = stream.ReadInt32();
                NameOffset = stream.ReadInt32();
                Contract.Assert(NameOffset < stream.Length);
#if UE4
                if (stream.UE4Version >= 516 && stream.Package.ContainsEditorData())
                {
                    LocalizationId = stream.ReadString();
                }

                if (stream.UE4Version >= 459)
                {
                    GatherableTextDataCount = stream.ReadInt32();
                    GatherableTextDataOffset = stream.ReadInt32();
                    Contract.Assert(GatherableTextDataOffset <= HeaderSize);
                }
#endif
                ExportCount = stream.ReadInt32();
                ExportOffset = stream.ReadInt32();
                Contract.Assert(ExportOffset < stream.Length);
#if APB
                if (stream.Package.Build == GameBuild.BuildName.APB &&
                    stream.LicenseeVersion >= 28)
                {
                    if (stream.LicenseeVersion >= 29)
                    {
                        stream.Skip(4);
                    }

                    stream.Skip(20);
                }
#endif
                ImportCount = stream.ReadInt32();
                ImportOffset = stream.ReadInt32();
                Contract.Assert(ImportOffset < stream.Length);

                Console.WriteLine("Names Count:" + NameCount + " Names Offset:" + NameOffset
                                  + " Exports Count:" + ExportCount + " Exports Offset:" + ExportOffset
                                  + " Imports Count:" + ImportCount + " Imports Offset:" + ImportOffset
                );

                if (stream.Version < (uint)PackageObjectLegacyVersion.HeritageTableDeprecated)
                {
                    HeritageCount = stream.ReadInt32();
                    Contract.Assert(HeritageCount > 0);

                    HeritageOffset = stream.ReadInt32();
                    Contract.Assert(HeritageOffset < stream.Length);

                    return;
                }
#if MIDWAY
                if (stream.Package.Build == GameBuild.BuildName.Stranglehold &&
                    stream.Version >= 375)
                {
                    stream.Read(out int _);
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedDependsTable)
                {
                    DependsOffset = stream.ReadInt32();
                    Debug.Assert(DependsOffset <= HeaderSize); // May be equal when there are no items.
                }
#if THIEF_DS || DEUSEX_IW
                if (stream.Package.Build == GameBuild.BuildName.Thief_DS ||
                    stream.Package.Build == GameBuild.BuildName.DeusEx_IW)
                {
                    //stream.Skip( 4 );
                    int unknown = stream.ReadInt32();
                    Console.WriteLine("Unknown:" + unknown);
                }
#endif
#if SPLINTERCELLX
                if (stream.Package.Build == BuildGeneration.SCX &&
                    stream.LicenseeVersion >= 12)
                {
                    // compiled-constant: SC1: 0xff0adde, SC3: DE AD F0 0F
                    stream.Read(out int uStack_10c);

                    // An FString converted to an FArray? Concatenating appUserName, appComputerName, appBaseDir, and appTimestamp.
                    stream.ReadArray(out UArray<byte> iStack_fc);
                }
#endif
#if LEAD
                if (stream.Package.Build == BuildGeneration.Lead &&
                    stream.LicenseeVersion >= 48)
                {
                    // Probably a new table, with v2c representing the count and v30 representing the offset.
                    stream.Read(out int v2c);
                    stream.Read(out int v30); // FileEndOffset, used in a loop that invokes a similar procedure as the names table.
                    Contract.Assert(v30 == stream.Length);
                    // v2c = 0 if v30 < ExportOffset

                    if (stream.LicenseeVersion >= 85)
                    {
                        stream.Read(out int v08); // same offset as the variable that is serialized before 'PackageFlags'.
                    }

                    goto skipGuid;
                }
#endif
                if (stream.UE4Version >= 384)
                {
                    StringAssetReferencesCount = stream.ReadInt32();
                    StringAssetReferencesOffset = stream.ReadInt32();
                    Contract.Assert(StringAssetReferencesOffset <= HeaderSize);
                }

                if (stream.UE4Version >= 510)
                {
                    SearchableNamesOffset = stream.ReadInt32();
                    Contract.Assert(SearchableNamesOffset <= HeaderSize);
                }

                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedImportExportGuidsTable &&
                    stream.UE4Version == 0
                    // FIXME: Correct the output version of these games instead.
#if BIOSHOCK
                    && stream.Package.Build != GameBuild.BuildName.Bioshock_Infinite
#endif
#if BORDERLANDS
                    && stream.Package.Build != GameBuild.BuildName.Borderlands_GOTYE
#endif
                   )
                {
                    ImportExportGuidsOffset = stream.ReadInt32();
                    Debug.Assert(ImportExportGuidsOffset <= HeaderSize);

                    ImportGuidsCount = stream.ReadInt32();
                    ExportGuidsCount = stream.ReadInt32();
                }
#if TRANSFORMERS
                if (stream.Package.Build == BuildGeneration.HMS &&
                    stream.Version >= 535)
                {
                    // ThumbnailTableOffset? But if so, the partial-upgrade must have skipped @AdditionalPackagesToCook
                    stream.Skip(4);

                    return;
                }
#endif
#if DD2
                // No version check found in the .exe
                if (stream.Package.Build == GameBuild.BuildName.DD2 && PackageFlags.HasFlag(PackageFlag.Cooked))
                    stream.Skip(4);
#endif
#if TERA
                if (stream.Package.Build == GameBuild.BuildName.Tera)
                {
                    goto skipThumbnailTableOffset;
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedThumbnailTable)
                {
                    ThumbnailTableOffset = stream.ReadInt32();
                    Debug.Assert(ThumbnailTableOffset <= HeaderSize);
                }
            skipThumbnailTableOffset:

#if SPELLBORN
                if (stream.Package.Build == GameBuild.BuildName.Spellborn
                    && stream.Version >= 148)
                {
                    goto skipGuid;
                }
#endif
                stream.ReadStruct(out Guid);
                Console.WriteLine("GUID:" + Guid);
            skipGuid:
#if MKKE
                if (stream.Package.Build == GameBuild.BuildName.MKKE)
                {
                    goto skipGenerations;
                }
#endif
#if UE4
                if (stream.Package.ContainsEditorData())
                {
                    if (stream.UE4Version >= 518)
                    {
                        stream.ReadStruct(out PersistentGuid);
                        if (stream.UE4Version < 520)
                        {
                            stream.ReadStruct(out OwnerPersistentGuid);
                        }
                    }
                    else
                    {
                        PersistentGuid = Guid;
                    }
                }
#endif
                int generationCount = stream.ReadInt32();
                Contract.Assert(generationCount >= 0);
                Console.WriteLine("Generations Count:" + generationCount);
#if APB
                // Guid, however only serialized for the first generation item.
                if (stream.Package.Build == GameBuild.BuildName.APB &&
                    stream.LicenseeVersion >= 32)
                {
                    stream.Skip(16);
                }
#endif
                stream.ReadArray(out Generations, generationCount);
            skipGenerations:
#if DNF
                if (stream.Package.Build == GameBuild.BuildName.DNF &&
                    stream.Version >= 151)
                {
                    if (PackageFlags.HasFlags(0x20U))
                    {
                        int buildMonth = stream.ReadInt32();
                        int buildYear = stream.ReadInt32();
                        int buildDay = stream.ReadInt32();
                        int buildSeconds = stream.ReadInt32();
                    }

                    string dnfString = stream.ReadString();

                    // DLC package
                    if (PackageFlags.HasFlags(0x80U))
                    {
                        // No additional data, just DLC authentication.
                    }
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedEngineVersion &&
                    stream.UE4Version == 0)
                {
                    // The Engine Version this package was created with
                    EngineVersion = stream.ReadInt32();
                    Console.WriteLine("EngineVersion:" + EngineVersion);
                }
#if UE4
                if (stream.UE4Version >= 336)
                {
                    // EngineVersion
                    PackageEngineVersion = new PackageFileEngineVersion();
                    PackageEngineVersion.Deserialize(stream);
                }

                if (stream.UE4Version >= 444)
                {
                    // Compatible EngineVersion
                    PackageCompatibleEngineVersion = new PackageFileEngineVersion();
                    PackageCompatibleEngineVersion.Deserialize(stream);
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedCookerVersion &&
                    stream.UE4Version == 0)
                {
                    // The Cooker Version this package was cooked with
                    CookerVersion = stream.ReadInt32();
                    Console.WriteLine("CookerVersion:" + CookerVersion);
                }
#if MASS_EFFECT
                if (stream.Package.Build == BuildGeneration.SFX)
                {
                    // Appears to be similar to a PackageFileEngineVersion

                    if (stream.LicenseeVersion >= 16 && stream.LicenseeVersion < 136)
                    {
                        stream.Read(out int _);
                    }

                    if (stream.LicenseeVersion >= 32 && stream.LicenseeVersion < 136)
                    {
                        stream.Read(out int _);
                    }

                    if (stream.LicenseeVersion >= 35 && stream.LicenseeVersion < 113)
                    {
                        stream.ReadMap(out UMap<string, UArray<string>> branch);
                        Console.WriteLine("Branch:" + branch);
                    }

                    if (stream.LicenseeVersion >= 37)
                    {
                        // Compiler-Constant ? 1
                        stream.Read(out int _);

                        // Compiler-Constant changelist? 1376256 (Mass Effect 1: LE)
                        stream.Read(out int _);
                    }

                    if (stream.LicenseeVersion >= 39 && stream.LicenseeVersion < 136)
                    {
                        stream.Read(out int _);
                    }
                }
#endif
                // Read compressed info?
                if (stream.Version >= (uint)PackageObjectLegacyVersion.CompressionAdded)
                {
                    CompressionFlags = stream.ReadUInt32();
                    Console.WriteLine("CompressionFlags:" + CompressionFlags);

                    stream.ReadArray(out CompressedChunks);
                }

                // SFX reads 392?
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedPackageSource)
                {
                    PackageSource = stream.ReadUInt32();
                    Console.WriteLine("PackageSource:" + PackageSource);
                }
#if MASS_EFFECT
                if (stream.Package.Build == BuildGeneration.SFX)
                {
                    if (stream.LicenseeVersion >= 44 && stream.LicenseeVersion < 136)
                    {
                        stream.Read(out int _);
                    }
                }
#endif
#if UE4
                if (stream.UE4Version > 0)
                {
                    return;
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedAdditionalPackagesToCook)
                {
#if TRANSFORMERS
                    if (stream.Package.Build == BuildGeneration.HMS)
                    {
                        return;
                    }
#endif
                    stream.ReadArray(out AdditionalPackagesToCook);
#if DCUO
                    if (stream.Package.Build == GameBuild.BuildName.DCUO)
                    {
                        var realNameOffset = (int)stream.Position;
                        Debug.Assert(
                            realNameOffset <= NameOffset,
                            "realNameOffset is > the parsed name offset for a DCUO package, we don't know where to go now!"
                        );

                        int offsetDif = NameOffset - realNameOffset;
                        NameOffset -= offsetDif;
                        ImportOffset -= offsetDif;
                        ExportOffset -= offsetDif;
                        DependsOffset = 0; // not working
                        ImportExportGuidsOffset -= offsetDif;
                        ThumbnailTableOffset -= offsetDif;
                    }
#endif
                }
#if BORDERLANDS
                if (stream.Package.Build == GameBuild.BuildName.Borderlands_GOTYE)
                {
                    return;
                }
#endif
#if BATTLEBORN
                if (stream.Package.Build == GameBuild.BuildName.Battleborn)
                {
                    // FIXME: Package format is being deserialized incorrectly and fails here.
                    stream.ReadUInt32();

                    return;
                }
#endif
                if (stream.Version >= (uint)PackageObjectLegacyVersion.AddedTextureAllocations)
                {
                    try
                    {
                        stream.ReadArray(out TextureAllocations);
                    }
                    catch (Exception exception)
                    {
                        // Errors shouldn't be fatal here because this feature is not necessary for our purposes.
                        LibServices.LogService.SilentException(new UnrealException("Couldn't parse TextureAllocations",
                            exception));
                    }
                }
#if ROCKETLEAGUE
                if (stream.Package.Build == GameBuild.BuildName.RocketLeague
                    && PackageFlags.HasFlag(PackageFlag.Cooked))
                {
                    int garbageSize = stream.ReadInt32();

                    int compressedChunkInfoOffset = stream.ReadInt32();
                    Debug.Assert(compressedChunkInfoOffset < stream.Length);

                    int lastBlockSize = stream.ReadInt32();
                    Debug.Assert(stream.Position == NameOffset, "There is more data before the NameTable");
                    // Data after this is encrypted
                }
#endif
#if UE4
                if (stream.UE4Version >= 112)
                {
                    stream.Read(out AssetRegistryDataOffset);
                }

                if (stream.UE4Version >= 212)
                {
                    stream.Read(out BulkDataOffset);
                }

                if (stream.UE4Version >= 224)
                {
                    stream.Read(out WorldTileInfoDataOffset);
                }

                if (stream.UE4Version >= 278)
                {
                    if (stream.UE4Version >= 326)
                    {
                        stream.Read(out ChunkIdentifiers);
                    }
                    else
                    {
                        stream.Read(out int chunkIdentifier);
                        ChunkIdentifiers = [chunkIdentifier];
                    }
                }

                if (stream.UE4Version >= 507)
                {
                    stream.Read(out PreloadDependencyCount);
                    stream.Read(out PreloadDependencyOffset);
                }
#endif
            }
        }

        public PackageFileSummary Summary;

        /// <summary>
        /// Whether the package was serialized in BigEndian encoding.
        /// </summary>
        public bool IsBigEndianEncoded { get; }

        [Obsolete] public const int VSIZEPREFIXDEPRECATED = 64;

        [Obsolete] public const int VINDEXDEPRECATED = 178;

        [Obsolete] public const int VDLLBIND = 655;

        [Obsolete] public const int VCLASSGROUP = 789;

        [Obsolete] public const int VCOOKEDPACKAGES = 277;

        public uint Version => Summary.Version;

        /// <summary>
        /// For debugging purposes. Change this to override the present Version deserialized from the package.
        /// </summary>
        public static ushort OverrideVersion;

        public ushort LicenseeVersion => Summary.LicenseeVersion;

        /// <summary>
        /// For debugging purposes. Change this to override the present Version deserialized from the package.
        /// </summary>
        public static ushort OverrideLicenseeVersion;

        [Obsolete("See Summary.PackageFlags")] public uint PackageFlags;

        [Obsolete("See Summary.HeaderSize")]
        public int HeaderSize => Summary.HeaderSize;

        [Obsolete("See Summary.FolderName")] public string Group;

        [Obsolete("See Summary.Guid")]
        public string GUID => Summary.Guid.ToString();

        [Obsolete("See Summary.Generations")]
        public UArray<UGenerationTableItem> Generations => Summary.Generations;

        [Obsolete("See Summary.EngineVersion")]
        public int EngineVersion => Summary.EngineVersion;

        [Obsolete("See Summary.CookerVersion")]
        public int CookerVersion => Summary.CookerVersion;

        [Obsolete("See Summary.CompressionFlags")]
        public uint CompressionFlags => Summary.CompressionFlags;

        [Obsolete("See Summary.CompressedChunks")]
        public UArray<CompressedChunk> CompressedChunks => Summary.CompressedChunks;

        /// <summary>
        /// List of unique unreal names.
        /// </summary>
        public List<UNameTableItem> Names { get; private set; } = [];

        /// <summary>
        /// List of info about exported objects.
        /// </summary>
        public List<UExportTableItem> Exports { get; private set; } = [];

        /// <summary>
        /// List of info about imported objects.
        /// </summary>
        public List<UImportTableItem> Imports { get; private set; } = [];

        /// <summary>
        /// A map of export to import indices for each export.
        /// </summary>
        public List<UArray<UPackageIndex>> Dependencies { get; private set; } = [];

        public UArray<PackageLevelGuid> ImportGuids { get; private set; } = [];
        public UMap<UGuid, UPackageIndex> ExportGuids { get; private set; } = new();

        public UArray<UObjectThumbnailTableItem> ObjectThumbnails { get; private set; } = [];

        #region Initialized Members

        /// <summary>
        /// Class types that should get added to the ObjectsList.
        /// </summary>
        private readonly Dictionary<string, Type> _ClassTypes = new();

        /// <summary>
        /// List of UObjects that were constructed by function ConstructObjects, later deserialized and linked.
        ///
        /// Includes Exports and Imports!.
        /// </summary>
        public List<UObject> Objects { get; private set; } = [];

        public NativesTablePackage NTLPackage;

        [Obsolete("See UPackageStream.Decoder", true)]
        public IBufferDecoder Decoder;

        #endregion

        #region Constructors

        /// <summary>
        /// A Collection of flags describing how a package should be initialized.
        /// </summary>
        [Flags]
        [Obfuscation(Exclude = true)]
        public enum InitFlags : ushort
        {
            Construct = 0x0001,
            Deserialize = 0x0002,
            [Obsolete] Import = 0x0004,
            Link = 0x0008,
            All = RegisterClasses | Construct | Deserialize | Link,
            RegisterClasses = 0x0010
        }

        [Obsolete]
        public static UnrealPackage DeserializePackage(string packagePath, FileAccess fileAccess = FileAccess.Read)
        {
            var stream = new UPackageStream(packagePath, FileMode.Open, fileAccess);
            var pkg = new UnrealPackage(stream);
            pkg.Deserialize(stream);
            return pkg;
        }

        private UPackage GetRootPackage(string filePath)
        {
            string packageName = Path.GetFileNameWithoutExtension(filePath);
            return FindObject<UPackage>(packageName)
                   ?? CreateObject<UPackage>(new UName(packageName), new UnrealFlags<ObjectFlag>(0));
        }

        /// <summary>
        /// Creates a new instance of the UELib.UnrealPackage class with a PackageStream and name.
        /// </summary>
        /// <param name="stream">A loaded UELib.PackageStream.</param>
        public UnrealPackage(UPackageStream stream)
        {
            _FullPackageName = stream.Name;
            Stream = stream;
            Stream.PostInit(this);

            // File Type
            // Signature is tested in UPackageStream
            RootPackage = GetRootPackage(_FullPackageName);
            IsBigEndianEncoded = stream.BigEndianCode;

        }

        /// <summary>
        /// Serializes the package header to the stream.
        /// </summary>
        public void Serialize()
        {
            Contract.Assert(Stream != null);
            Serialize(Stream);
        }

        /// <summary>
        /// Serializes the package header to the output stream.
        /// </summary>
        /// <param name="stream">the output stream.</param>
        public void Serialize(IUnrealStream stream)
        {
            Summary.Serialize(stream);

            Branch.PostSerializeSummary(this, stream, ref Summary);
            Debug.Assert(stream.Serializer != null,
                "IUnrealStream.Serializer cannot be null. Did you forget to initialize the Serializer in PostSerializeSummary?");

            if (Summary.CompressedChunks?.Count > 0 &&
                Summary.CompressionFlags != 0)
            {
                throw new NotImplementedException("Cannot serialize compressed package data.");
            }

            if (Summary.Heritages?.Count > 0
                && stream.Version < (uint)PackageObjectLegacyVersion.HeritageTableDeprecated)
            {
                if (Summary.HeritageOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.HeritageOffset);
                    stream.Seek(Summary.HeritageOffset, SeekOrigin.Begin);
                }

                SerializeHeritages(stream);
            }
            else
            {
                Summary.HeritageCount = 0;
                Summary.HeritageOffset = 0;
            }

            if (Names.Count > 0)
            {
                if (Summary.NameOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.NameOffset);
                    stream.Seek(Summary.NameOffset, SeekOrigin.Begin);
                }

                SerializeNames(stream);
            }
            else
            {
                Summary.NameCount = 0;
                Summary.NameOffset = 0;
            }

            if (Imports.Count > 0)
            {
                if (Summary.ImportOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.ImportOffset);
                    stream.Seek(Summary.ImportOffset, SeekOrigin.Begin);
                }

                SerializeImports(stream);
            }
            else
            {
                Summary.ImportCount = 0;
                Summary.ImportOffset = 0;
            }

            if (Exports.Count > 0)
            {
                if (Summary.ExportOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.ExportOffset);
                    stream.Seek(Summary.ExportOffset, SeekOrigin.Begin);
                }

                SerializeExports(stream);
            }
            else
            {
                Summary.ExportCount = 0;
                Summary.ExportOffset = 0;
            }

            if (Dependencies.Count > 0
                && stream.Version >= (uint)PackageObjectLegacyVersion.AddedDependsTable)
            {
                if (Summary.DependsOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.DependsOffset);
                    stream.Seek(Summary.DependsOffset, SeekOrigin.Begin);
                }

                SerializeDependencies(stream);
            }
            else
            {
                Summary.DependsOffset = 0;
            }

            if ((ImportGuids.Count > 0 || ExportGuids.Count > 0)
                && stream.Version >= (uint)PackageObjectLegacyVersion.AddedImportExportGuidsTable
                && stream.UE4Version == 0)
            {
                if (Summary.ImportExportGuidsOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.ImportExportGuidsOffset);
                    stream.Seek(Summary.ImportExportGuidsOffset, SeekOrigin.Begin);
                }

                SerializeImportExportGuids(stream);
            }
            else
            {
                Summary.ImportGuidsCount = 0;
                Summary.ExportGuidsCount = 0;
                Summary.ImportExportGuidsOffset = 0;
            }

            // The count is at the offset, so we have to serialize this regardless of the thumbnail count.
            if (ObjectThumbnails.Count > 0
                && stream.Version >= (uint)PackageObjectLegacyVersion.AddedThumbnailTable)
            {
                if (Summary.ThumbnailTableOffset != 0)
                {
                    Contract.Assert(stream.Position < Summary.ThumbnailTableOffset);
                    stream.Seek(Summary.ThumbnailTableOffset, SeekOrigin.Begin);
                }

                SerializeThumbnails(stream);
            }
            else
            {
                Summary.ThumbnailTableOffset = 0;
            }

            if (stream.UE4Version >= 384)
            {
                throw new NotSupportedException("Cannot yet serialize UE4 StringAssetReferences");
            }
            else
            {
                Summary.StringAssetReferencesCount = 0;
                Summary.StringAssetReferencesOffset = 0;
            }

            if (stream.UE4Version >= 510)
            {
                throw new NotSupportedException("Cannot yet serialize UE4 SearchableNames");
            }
            else
            {
                Summary.SearchableNamesOffset = 0;
            }

            Branch.PostSerializePackage(stream.Package, stream);
            Summary.HeaderSize = (int)stream.Position;
        }

        /// <summary>
        /// Deserializes the package header from the stream.
        /// </summary>
        public void Deserialize()
        {
            Contract.Assert(Stream != null);
            Deserialize(Stream);
        }

        /// <summary>
        /// Deserializes the package header from the input stream.
        /// </summary>
        /// <param name="stream">the input stream.</param>
        public void Deserialize(IUnrealStream stream)
        {
            BinaryMetaData.Fields.Clear();

            // Reset all previously-deserialized data if any.
            uint tag = Summary.Tag;
            Summary = new PackageFileSummary
            {
                Tag = tag
            };
            Summary.Deserialize(stream);
            BinaryMetaData.AddField(nameof(Summary), Summary, 0, stream.Position);

            // FIXME: For backwards compatibility.
            PackageFlags = (uint)Summary.PackageFlags;
            Group = Summary.FolderName;
            Branch.PostDeserializeSummary(this, stream, ref Summary);
            Debug.Assert(stream.Serializer != null,
                "IUnrealStream.Serializer cannot be null. Did you forget to initialize the Serializer in PostDeserializeSummary?");

            // We can't continue without decompressing.
            if (Summary.CompressedChunks != null &&
                Summary.CompressedChunks.Any())
            {
                if (Summary.CompressionFlags != 0)
                {
                    return;
                }

                // Flags 0? Let's pretend that we no longer possess any chunks.
                Summary.CompressedChunks.Clear();
            }

            // Read the heritages
            if (Summary.HeritageCount > 0)
            {
                stream.Seek(Summary.HeritageOffset, SeekOrigin.Begin);
                DeserializeHeritages(stream);
            }
#if TERA
            if (Build == GameBuild.BuildName.Tera) Summary.NameCount = Generations.Last().NameCount;
#endif
            // Read the names
            if (Summary.NameCount > 0)
            {
                stream.Seek(Summary.NameOffset, SeekOrigin.Begin);
                DeserializeNames(stream);
            }

            // Read the imports
            if (Summary.ImportCount > 0)
            {
                stream.Seek(Summary.ImportOffset, SeekOrigin.Begin);
                DeserializeImports(stream);
            }

            // Read the exports
            if (Summary.ExportCount > 0)
            {
                stream.Seek(Summary.ExportOffset, SeekOrigin.Begin);
                DeserializeExports(stream);
            }

            // Read the dependencies
            if (Summary.DependsOffset > 0)
            {
                stream.Seek(Summary.DependsOffset, SeekOrigin.Begin);

                try
                {
                    DeserializeDependencies(stream);
                }
                catch (Exception exception)
                {
                    // Errors shouldn't be fatal here because this feature is not necessary for our purposes.
                    LibServices.LogService.SilentException(new UnrealException("Couldn't parse DependenciesTable", exception));
                }
            }

            // Read the import and export guids
            if (Summary.ImportExportGuidsOffset > 0)
            {
                stream.Seek(Summary.ImportExportGuidsOffset, SeekOrigin.Begin);

                try
                {
                    DeserializeImportExportGuids(stream);
                }
                catch (Exception exception)
                {
                    // Errors shouldn't be fatal here because this feature is not necessary for our purposes.
                    LibServices.LogService.SilentException(new UnrealException("Couldn't parse ImportExportGuidsTable", exception));
                }
            }

            // Read the object thumbnails
            if (Summary.ThumbnailTableOffset > 0)
            {
                stream.Seek(Summary.ThumbnailTableOffset, SeekOrigin.Begin);

                try
                {
                    DeserializeThumbnails(stream);
                }
                catch (Exception exception)
                {
                    // Errors shouldn't be fatal here because this feature is not necessary for our purposes.
                    LibServices.LogService.SilentException(new UnrealException("Couldn't parse ThumbnailTable", exception));
                }
            }

            Branch.PostDeserializePackage(stream.Package, stream);

            if (Summary.HeaderSize == 0)
            {
                Summary.HeaderSize = (int)stream.Position;
            }

            if (Summary.HeaderSize != stream.Position)
            {
                LibServices.LogService.SilentException(new UnrealException("Missing package header data, serialization may fail."));
            }
        }

        /// <summary>
        /// Serializes all the collected package dependencies to a stream.
        /// </summary>
        /// <param name="stream">the output stream.</param>
        private void SerializeDependencies(IUnrealStream stream)
        {
            Summary.DependsOffset = (int)stream.Position;

            int dependsCount = Exports.Count;
#if BIOSHOCK
            // FIXME: Version?
            if (Build == GameBuild.BuildName.Bioshock_Infinite)
            {
                stream.Write(dependsCount);
            }
#endif
            Contract.Assert(dependsCount <= Dependencies.Count);
            for (int exportIndex = 0; exportIndex < dependsCount; ++exportIndex)
            {
                var imports = Dependencies[exportIndex];
                stream.Write(imports.Count);
                foreach (var packageIndex in imports)
                {
                    stream.Write((int)packageIndex);
                }
            }
        }

        private void DeserializeDependencies(IUnrealStream stream)
        {
            int dependsCount = Summary.ExportCount;
#if BIOSHOCK
            // FIXME: Version?
            if (Build == GameBuild.BuildName.Bioshock_Infinite)
            {
                dependsCount = stream.ReadInt32();
            }
#endif
            Dependencies = new List<UArray<UPackageIndex>>(dependsCount);
            for (int exportIndex = 0; exportIndex < dependsCount; ++exportIndex)
            {
                stream.Read(out int c);
                var importIndexes = new UArray<UPackageIndex>(c);
                for (int i = 0; i < c; ++i)
                {
                    stream.Read(out int packageIndex);
                    importIndexes.Add(packageIndex);
                }

                Dependencies.Add(importIndexes);
            }

            BinaryMetaData.AddField(nameof(Dependencies), Dependencies, Summary.DependsOffset,
                stream.Position - Summary.DependsOffset);
        }

        /// <summary>
        /// Serializes all the collected package heritages to a stream.
        /// </summary>
        /// <param name="stream">the output stream.</param>
        private void SerializeHeritages(IUnrealStream stream)
        {
            Summary.HeritageOffset = (int)stream.Position;
            Summary.HeritageCount = Summary.Heritages.Count;

            stream.WriteArray(Summary.Heritages);
        }

        /// <summary>
        /// Deserializes all the package heritages from a stream.
        /// </summary>
        /// <param name="stream">the input stream.</param>
        private void DeserializeHeritages(IUnrealStream stream)
        {
            stream.ReadArray(out Summary.Heritages, Summary.HeritageCount);

            if (Summary.Heritages.Count > 0)
            {
                Summary.Guid = Summary.Heritages.Last();
            }

            BinaryMetaData.AddField(nameof(Summary.Heritages), Summary.Heritages, Summary.HeritageOffset,
                stream.Position - Summary.HeritageOffset);
        }

        /// <summary>
        /// Serializes all the collected package names to a stream.
        /// </summary>
        /// <param name="stream">the output stream.</param>
        private void SerializeNames(IUnrealStream stream)
        {
            Summary.NameOffset = (int)stream.Position;
            Summary.NameCount = Names.Count;
#if SPELLBORN
            if (Build == GameBuild.BuildName.Spellborn
                && Names[0].Name == "None")
                Names[0].Name = "DRFORTHEWIN";
#endif
            var serializer = stream.Serializer;
            foreach (var nameEntry in Names)
            {
                nameEntry.Offset = (int)stream.Position;
                serializer.Serialize(stream, nameEntry);
                nameEntry.Size = (int)(stream.Position - nameEntry.Offset);
            }
#if SPELLBORN
            if (Build == GameBuild.BuildName.Spellborn
                && Names[0].Name == "DRFORTHEWIN")
                Names[0].Name = "None";
#endif
        }

        /// <summary>
        /// Deserializes all the package names from a stream.
        /// </summary>
        /// <param name="stream">the input stream.</param>
        private void DeserializeNames(IUnrealStream stream)
        {
            var serializer = stream.Serializer;

            Names = new List<UNameTableItem>(Summary.NameCount);
            for (var i = 0; i < Summary.NameCount; ++i)
            {
                var nameEntry = new UNameTableItem { Offset = (int)stream.Position, Index = i };
                serializer.Deserialize(stream, nameEntry);
                nameEntry.Size = (int)(stream.Position - nameEntry.Offset);
                Names.Add(nameEntry);
            }

            BinaryMetaData.AddField(nameof(Names), Names, Summary.NameOffset, stream.Position - Summary.NameOffset);
#if SPELLBORN
            // WTF were they thinking? Change DRFORTHEWIN to None
            if (Build == GameBuild.BuildName.Spellborn
                && Names[0].Name == "DRFORTHEWIN")
                Names[0].Name = "None";
            // False??
            //Debug.Assert(stream.Position == Summary.ImportsOffset);
#endif
        }

        /// <summary>
        /// Serializes all the collected package imports to a stream.
        /// </summary>
        /// <param name="stream">the output stream.</param>
        private void SerializeImports(IUnrealStream stream)
        {
            Summary.ImportOffset = (int)stream.Position;
            Summary.ImportCount = Imports.Count;

            var serializer = stream.Serializer;
            foreach (var imp in Imports)
            {
                imp.Offset = (int)stream.Position;
                serializer.Serialize(stream, imp);
                imp.Size = (int)(stream.Position - imp.Offset);
            }
        }

        /// <summary>
        /// Deserializes all the package imports from a stream.
        /// </summary>
        /// <param name="stream">the input stream.</param>
        private void DeserializeImports(IUnrealStream stream)
        {
            var serializer = stream.Serializer;

            Imports = new List<UImportTableItem>(Summary.ImportCount);
            for (var i = 0; i < Summary.ImportCount; ++i)
            {
                var imp = new UImportTableItem { Offset = (int)stream.Position, Index = i, Owner = this };
                serializer.Deserialize(stream, imp);
                imp.Size = (int)(stream.Position - imp.Offset);
                Imports.Add(imp);
            }

            BinaryMetaData.AddField(nameof(Imports), Imports, Summary.ImportOffset,
                stream.Position - Summary.ImportOffset);
        }

        /// <summary>
        /// Serializes all the collected package exports to a stream.
        /// </summary>
        /// <param name="stream">the output stream.</param>
        private void SerializeExports(IUnrealStream stream)
        {
            Summary.ExportOffset = (int)stream.Position;
            Summary.ExportCount = Exports.Count;

            var serializer = stream.Serializer;
            foreach (var exp in Exports)
            {
                exp.Offset = (int)stream.Position;
                serializer.Serialize(stream, exp);
                exp.Size = (int)(stream.Position - exp.Offset);
            }
        }

        /// <summary>
        /// Deserializes all the package exports from a stream.
        /// </summary>
        /// <param name="stream">the input stream.</param>
        private void DeserializeExports(IUnrealStream stream)
        {
            var serializer = stream.Serializer;

            Exports = new List<UExportTableItem>(Summary.ExportCount);
            for (var i = 0; i < Summary.ExportCount; ++i)
            {
                var exp = new UExportTableItem { Offset = (int)stream.Position, Index = i, Owner = this };
                serializer.Deserialize(stream, exp);
                exp.Size = (int)(stream.Position - exp.Offset);
                Exports.Add(exp);
            }

            BinaryMetaData.AddField(nameof(Exports), Exports, Summary.ExportOffset,
                stream.Position - Summary.ExportOffset);
        }

        private void SerializeImportExportGuids(IUnrealStream stream)
        {
            Summary.ImportExportGuidsOffset = (int)stream.Position;

            Summary.ImportGuidsCount = ImportGuids.Count;
            foreach (var importGuid in ImportGuids)
            {
                var guid = importGuid;
                stream.Write(ref guid);
            }

            Summary.ExportGuidsCount = ExportGuids.Count;
            foreach (var exportGuid in ExportGuids)
            {
                var guidKey = exportGuid.Key;
                stream.Write(ref guidKey);
                stream.Write((int)exportGuid.Value);
            }
        }

        private void DeserializeImportExportGuids(IUnrealStream stream)
        {
            ImportGuids = new UArray<PackageLevelGuid>(Summary.ImportGuidsCount);
            for (var i = 0; i < Summary.ImportGuidsCount; ++i)
            {
                stream.ReadStruct(out PackageLevelGuid importGuid);
                ImportGuids[i] = importGuid;
            }

            ExportGuids = new UMap<UGuid, UPackageIndex>(Summary.ExportGuidsCount);
            for (var i = 0; i < Summary.ExportGuidsCount; ++i)
            {
                stream.ReadStruct(out UGuid objectGuid);
                stream.Read(out int exportIndex);

                ExportGuids.Add(objectGuid, exportIndex);
            }

            if (stream.Position != Summary.ImportExportGuidsOffset)
            {
                BinaryMetaData.AddField("ImportExportGuids", null, Summary.ImportExportGuidsOffset,
                    stream.Position - Summary.ImportExportGuidsOffset);
            }
        }

        private void SerializeThumbnails(IUnrealStream stream)
        {
            // If true then we are writing a new package, otherwise just overwrite existing data.
            if (Summary.ThumbnailTableOffset == 0)
            {
                // Write the thumbnail data somewhere, keep track of the position for the thumbnail item to reference.
                foreach (var item in ObjectThumbnails)
                {
                    item.ThumbnailOffset = (int)stream.Position;
                    stream.Write(ref item.Thumbnail);
                }
            }

            Summary.ThumbnailTableOffset = (int)stream.Position;

            // Write the thumbnail paths and offsets
            stream.Write(ObjectThumbnails.Count);
            foreach (var thumb in ObjectThumbnails)
            {
                thumb.Offset = (int)stream.Position;
                thumb.Deserialize(stream);
                thumb.Size = (int)(stream.Position - thumb.Offset);
            }
        }

        private void DeserializeThumbnails(IUnrealStream stream)
        {
            stream.Read(out int thumbnailCount);
            ObjectThumbnails = new UArray<UObjectThumbnailTableItem>(thumbnailCount);
            for (var i = 0; i < thumbnailCount; ++i)
            {
                var thumb = new UObjectThumbnailTableItem { Offset = (int)stream.Position, Index = i };
                thumb.Deserialize(stream);
                thumb.Size = (int)(stream.Position - thumb.Offset);
                ObjectThumbnails.Add(thumb);
            }

            BinaryMetaData.AddField("Thumbnails", null, Summary.ThumbnailTableOffset,
                stream.Position - Summary.ThumbnailTableOffset);
        }

        /// <summary>
        /// Constructs all export objects.
        /// </summary>
        /// <param name="initFlags">Initializing rules such as deserializing and/or linking.</param>
        public void InitializeExportObjects(InitFlags initFlags = InitFlags.All)
        {
            Objects = new List<UObject>(Exports.Count);
            foreach (var exp in Exports) CreateObject(exp);

            if ((initFlags & InitFlags.Deserialize) == 0)
                return;

            DeserializeObjects();
            if ((initFlags & InitFlags.Link) != 0) LinkObjects();
        }

        /// <summary>
        /// Constructs all import objects.
        /// </summary>
        /// <param name="initialize">If TRUE initialize all constructed objects.</param>
        [Obsolete("Pending deprecation")]
        public void InitializeImportObjects(bool initialize = true)
        {
            Objects = new List<UObject>(Imports.Count);
            foreach (var imp in Imports) CreateObject(imp);

            if (!initialize) return;

            foreach (var obj in Objects) obj.PostInitialize();
        }

        /// <summary>
        /// Initializes all the objects that resist in this package as well tries to import deserialized data from imported objects.
        /// </summary>
        /// <param name="initFlags">A collection of initializing flags to notify what should be initialized.</param>
        /// <example>InitializePackage( UnrealPackage.InitFlags.All )</example>
        public void InitializePackage(InitFlags initFlags = InitFlags.All)
        {
            if ((initFlags & InitFlags.RegisterClasses) != 0) RegisterExportedClassTypes();

            if ((initFlags & InitFlags.Construct) == 0)
            {
                return;
            }

            ConstructObjects();
            if ((initFlags & InitFlags.Deserialize) == 0)
                return;

            try
            {
                DeserializeObjects();
            }
            catch (Exception ex)
            {
                throw new UnrealException("Deserialization", ex);
            }

            try
            {
                if ((initFlags & InitFlags.Link) != 0) LinkObjects();
            }
            catch (Exception ex)
            {
                throw new UnrealException("Linking", ex);
            }
        }

        /// <summary>
        ///
        /// </summary>
        public class PackageEventArgs : EventArgs
        {
            /// <summary>
            /// Event identification.
            /// </summary>
            public enum Id : byte
            {
                /// <summary>
                /// Constructing Export/Import objects.
                /// </summary>
                Construct = 0,

                /// <summary>
                /// Deserializing objects.
                /// </summary>
                Deserialize = 1,

                /// <summary>
                /// Importing objects from linked packages.
                /// </summary>
                [Obsolete] Import = 2,

                /// <summary>
                /// Connecting deserialized object indexes.
                /// </summary>
                Link = 3,

                /// <summary>
                /// Deserialized a Export/Import object.
                /// </summary>
                Object = 0xFF
            }

            /// <summary>
            /// The event identification.
            /// </summary>
            public readonly Id EventId;

            /// <summary>
            /// Constructs a new event with @eventId.
            /// </summary>
            /// <param name="eventId">Event identification.</param>
            public PackageEventArgs(Id eventId)
            {
                EventId = eventId;
            }
        }

        /// <summary>
        ///
        /// </summary>
        public event PackageEventHandler? NotifyPackageEvent;

        private void OnNotifyPackageEvent(PackageEventArgs e)
        {
            NotifyPackageEvent?.Invoke(this, e);
        }

        /// <summary>
        /// Called when an object is added to the ObjectsList via the AddObject function.
        /// </summary>
        public event NotifyObjectAddedEventHandler? NotifyObjectAdded;

        /// <summary>
        /// Constructs all the objects based on data from _ExportTableList and _ImportTableList, and
        /// all constructed objects are added to the _ObjectsList.
        /// </summary>
        private void ConstructObjects()
        {
            LibServices.Debug("Constructing all package objects", PackageName);

            Objects = new List<UObject>(Imports.Count + Exports.Count);
            OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Construct));
            foreach (var imp in Imports)
                try
                {
                    if (imp.Object != null) continue;
                    CreateObject(imp);
                }
                catch (Exception exc)
                {
                    throw new UnrealException("couldn't create import object for " + imp, exc);
                }

            foreach (var exp in Exports)
                try
                {
                    if (exp.Object != null) continue;
                    CreateObject(exp);
                }
                catch (Exception exc)
                {
                    throw new UnrealException("couldn't create export object for " + exp, exc);
                }
        }

        /// <summary>
        /// Deserializes all exported objects.
        /// </summary>
        private void DeserializeObjects()
        {
            LibServices.Debug("Loading all package objects", PackageName);

            // Only exports should be deserialized and PostInitialized!
            OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Deserialize));
            foreach (var exp in Exports)
            {
                if (exp.Object is not UnknownObject)
                {
                    if (exp.Object!.DeserializationState == 0 && !exp.Object.ShouldDeserializeOnDemand)
                    {
                        exp.Object.Load();
                    }
                }

                OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Object));
            }
        }

        /// <summary>
        /// Initializes all exported objects.
        /// </summary>
        private void LinkObjects()
        {
            LibServices.Debug("Linking all package objects", PackageName);

            // Notify that deserializing is done on all objects, now objects can read properties that were dependent on deserializing
            OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Link));
            foreach (var exp in Exports)
                try
                {
                    if (!(exp.Object is UnknownObject)) exp.Object!.PostInitialize();
                }
                finally
                {
                    OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Object));
                }
        }

        private void RegisterExportedClassTypes()
        {
            var exportedTypes = Assembly.GetExecutingAssembly().GetExportedTypes();
            foreach (var exportedType in exportedTypes)
            {
                object[] attributes = exportedType.GetCustomAttributes(typeof(UnrealRegisterClassAttribute), false);
                if (attributes.Length == 1) AddClassType(exportedType.Name.Substring(1), exportedType);
            }
        }

        #endregion

        #region Methods

        // Create pseudo objects for imports so that we have non-null references to imports.
        private UObject CreateObject(UImportTableItem import)
        {
            if (import.Object != null)
            {
                return import.Object;
            }

            var classType = GetClassType(import.ClassName);
            var obj = (UObject)Activator.CreateInstance(classType);
            Debug.Assert(obj != null);
            obj.Name = import.ObjectName;
            obj.ObjectFlags = new UnrealFlags<ObjectFlag>(Branch.EnumFlagsMap[typeof(ObjectFlag)], ObjectFlag.Public);
            obj.Package = this;
            obj.PackageIndex = -(import.Index + 1);
            obj.Table = import;
            obj.Class = null; // FindObject<UClass> ?? StaticClass
            obj.Outer = IndexToObject(import.OuterIndex); // ?? ClassPackage
            import.Object = obj;

            AddToObjects(obj);
            OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Object));

            return obj;
        }

        private UObject CreateObject(UExportTableItem export)
        {
            Debug.Assert(export.Object == null);

            var objName = export.ObjectName;
            var objSuper = IndexToObject<UStruct>(export.SuperIndex);

            var objOuter = IndexToObject<UObject>(export.OuterIndex);
            if (objOuter == null && (export.ExportFlags & (uint)ExportFlags.ForcedExport) != 0)
            {
                var pkg = FindObject<UPackage>(objName) ?? CreateObject<UPackage>(objName);
                export.Object = pkg;

                return pkg;
            }

            var objClass = IndexToObject<UClass>(export.ClassIndex);

            var internalClassType = GetClassType(objClass?.Name ?? "Class");
            if (objClass != null && internalClassType == typeof(UnknownObject) && (int)objClass > 0)
            {
                // Try one of the "super" classes for unregistered classes.
                internalClassType = objClass
                    .EnumerateSuper<UClass>()
                    .Select(cls => GetClassType(cls.Name))
                    .FirstOrDefault() ?? typeof(UnknownObject);
            }

            var obj = (UObject)Activator.CreateInstance(internalClassType);
            Debug.Assert(obj != null);
            obj.Name = objName;
            obj.ObjectFlags = new UnrealFlags<ObjectFlag>(export.ObjectFlags, Branch.EnumFlagsMap[typeof(ObjectFlag)]);
            obj.Package = this;
            obj.PackageIndex = export.Index + 1;
            obj.Table = export;
            obj.Class = objClass; // ?? StaticClass
            obj.Outer = objOuter ?? RootPackage;

            if (obj is UStruct uStruct)
            {
                uStruct.Super = objSuper;
            }

            export.Object = obj;

            AddToObjects(obj);
            OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Object));

            return obj;
        }

        private T CreateObject<T>(UName objectName) where T : UObject
        {
            return CreateObject<T>(objectName, new UnrealFlags<ObjectFlag>());
        }

        private T CreateObject<T>(UName objectName, UnrealFlags<ObjectFlag> objectFlags) where T : UObject
        {
            var obj = (T)Activator.CreateInstance(typeof(T));
            Debug.Assert(obj != null);
            obj.ObjectFlags = objectFlags;
            obj.Package = this;
            obj.PackageIndex = 0;
            obj.Table = null;
            obj.Name = objectName;

            AddToObjects(obj);
            OnNotifyPackageEvent(new PackageEventArgs(PackageEventArgs.Id.Object));

            return obj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToObjects(UObject obj)
        {
            Objects.Add(obj);
            NotifyObjectAdded?.Invoke(this, new ObjectEventArgs(obj));
        }

        [Obsolete("Use Summary.Serialize")]
        public void WritePackageFlags()
        {
            Stream.Position = 8;
            Stream.Write(PackageFlags);
        }

        [Obsolete("Use AddClassType")]
        public void RegisterClass(string className, Type classObject)
        {
            AddClassType(className, classObject);
        }

        public void AddClassType(string className, Type classObject)
        {
            _ClassTypes[className.ToLower()] = classObject;
        }

        public Type GetClassType(string className)
        {
            _ClassTypes.TryGetValue(className.ToLower(), out var classType);
            return classType ?? typeof(UnknownObject);
        }

        public bool HasClassType(string className)
        {
            return _ClassTypes.ContainsKey(className.ToLower());
        }

        [Obsolete("Use HasClassType")]
        public bool IsRegisteredClass(string className)
        {
            return HasClassType(className);
        }

        [Obsolete("Use IndexToObject")]
        public UObject? GetIndexObject(int packageIndex) => IndexToObject<UObject>(packageIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UObject? IndexToObject(int packageIndex) => IndexToObject<UObject>(packageIndex);

        /// <summary>
        /// Returns a <see cref="UObject"/> from a package index.
        /// </summary>
        public T? IndexToObject<T>(int packageIndex)
            where T : UObject
        {
            switch (packageIndex)
            {
                case < 0:
                    {
                        var import = Imports[-packageIndex - 1];
                        return (T?)import.Object ?? (T)CreateObject(import);
                    }

                case > 0:
                    {
                        var export = Exports[packageIndex - 1];
                        return (T?)export.Object ?? (T)CreateObject(export);
                    }

                default:
                    return null;
            }
        }

        [Obsolete]
        public string GetIndexObjectName(int packageIndex)
        {
            return IndexToObjectResource(packageIndex)!.ObjectName;
        }

        [Obsolete]
        public string GetIndexName(int nameIndex)
        {
            return Names[nameIndex].Name;
        }

        [Obsolete("Use IndexToObjectResource")]
        public UObjectTableItem? GetIndexTable(int packageIndex) => IndexToObjectResource(packageIndex);

        /// <summary>
        /// Returns a <see cref="UObjectTableItem"/> from a package index.
        /// </summary>
        public UObjectTableItem? IndexToObjectResource(int packageIndex)
        {
            return packageIndex switch
            {
                < 0 => Imports[-packageIndex - 1],
                > 0 => Exports[packageIndex - 1],
                _ => null
            };
        }

        [Obsolete("See below")]
        public UObject? FindObject(string objectName, Type classType, bool checkForSubclass = false)
        {
            var obj = Objects.Find(o => string.Compare(o.Name, objectName, StringComparison.OrdinalIgnoreCase) == 0 &&
                                        (checkForSubclass
                                            ? o.GetType().IsSubclassOf(classType)
                                            : o.GetType() == classType));
            return obj;
        }

        public T? FindObject<T>(string objectName, bool checkForSubclass = false) where T : UObject
        {
            var obj = Objects.Find(o => string.Compare(o.Name, objectName, StringComparison.OrdinalIgnoreCase) == 0 &&
                                        (checkForSubclass
                                            ? o.GetType().IsSubclassOf(typeof(T))
                                            : o.GetType() == typeof(T)));
            return (T)obj;
        }

        public UObject? FindObjectByGroup(string objectGroup)
        {
            string[] groups = objectGroup.Split('.');
            string? objectName = groups.LastOrDefault();
            if (objectName == null)
            {
                return null;
            }

            groups = groups.Take(groups.Length - 1).Reverse().ToArray();
            var foundObj = Objects.Find(obj =>
            {
                if (string.Compare(obj.Name, objectName, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }

                var outer = obj.Outer;
                foreach (string group in groups)
                {
                    if (outer == null)
                    {
                        return false;
                    }

                    if (string.Compare(outer.Name, group, StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        return false;
                    }

                    outer = outer.Outer;
                }

                return true;
            });

            return foundObj;
        }

        /// <summary>
        /// If true, the package won't have any editor data such as HideCategories, ScriptText etc.
        ///
        /// However this condition is not only determined by the package flags property.
        /// Thus it is necessary to explicitly indicate this state.
        /// </summary>
        /// <returns>Whether package is cooked for consoles.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsConsoleCooked()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.Cooked)
                   && CookerPlatform == BuildPlatform.Console;
        }

        /// <summary>
        /// Checks whether this package is marked with @flags.
        /// </summary>
        /// <param name="flags">The enum @flag to test.</param>
        /// <returns>Whether this package is marked with @flag.</returns>
        [Obsolete("See Summary.PackageFlags.HasFlag")]
        public bool HasPackageFlag(PackageFlags flags)
        {
            return Summary.PackageFlags.HasFlags((uint)flags);
        }

        /// <summary>
        /// Checks whether this package is marked with @flags.
        /// </summary>
        /// <param name="flags">The uint @flag to test</param>
        /// <returns>Whether this package is marked with @flag.</returns>
        [Obsolete("See Summary.PackageFlags.HasFlag")]
        public bool HasPackageFlag(uint flags)
        {
            return (PackageFlags & flags) != 0;
        }

        /// <summary>
        /// Tests the packageflags of this UELib.UnrealPackage instance whether it is cooked.
        /// </summary>
        /// <returns>True if cooked or False if not.</returns>
        [Obsolete]
        public bool IsCooked()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.Cooked);
        }

        /// <summary>
        /// Checks for the Map flag in PackageFlags.
        /// </summary>
        /// <returns>Whether if this package is a map.</returns>
        [Obsolete]
        public bool IsMap()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.ContainsMap);
        }

        /// <summary>
        /// Checks if this package contains code classes.
        /// </summary>
        /// <returns>Whether if this package contains code classes.</returns>
        [Obsolete]
        public bool IsScript()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.ContainsScript);
        }

        /// <summary>
        /// Checks if this package was built using the debug configuration.
        /// </summary>
        /// <returns>Whether if this package was built in debug configuration.</returns>
        [Obsolete]
        public bool IsDebug()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.ContainsDebugData);
        }

        /// <summary>
        /// Checks for the Stripped flag in PackageFlags.
        /// </summary>
        /// <returns>Whether if this package is stripped.</returns>
        [Obsolete]
        public bool IsStripped()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.StrippedSource);
        }

        /// <summary>
        /// Tests the packageflags of this UELib.UnrealPackage instance whether it is encrypted.
        /// </summary>
        /// <returns>True if encrypted or False if not.</returns>
        [Obsolete]
        public bool IsEncrypted()
        {
            return Summary.PackageFlags.HasFlag(PackageFlag.Encrypted);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsEditorData()
        {
            return Summary.UE4Version > 0 && !Summary.PackageFlags.HasFlag(PackageFlag.FilterEditorOnly);
        }

        #region IBuffered

        public byte[] CopyBuffer()
        {
            byte[] buff = new byte[Summary.HeaderSize];
            Stream.Seek(0, SeekOrigin.Begin);
            Stream.EndianAgnosticRead(buff, 0, Summary.HeaderSize);

            return buff;
        }

        public IUnrealStream GetBuffer()
        {
            return Stream;
        }

        public int GetBufferPosition()
        {
            return 0;
        }

        public int GetBufferSize()
        {
            return HeaderSize;
        }

        public string GetBufferId(bool fullName = false)
        {
            return fullName ? FullPackageName : PackageName;
        }

        #endregion

        /// <inheritdoc/>
        public override string ToString()
        {
            return PackageName;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Objects != null && Objects.Any())
            {
                foreach (var obj in Objects) obj.Dispose();

                Objects.Clear();
                Objects = null;
            }

            Stream?.Dispose();
        }

        #endregion
    }
}
