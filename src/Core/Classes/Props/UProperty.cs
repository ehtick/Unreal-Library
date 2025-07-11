﻿using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using UELib.Branch;
using UELib.Flags;
using UELib.Types;

namespace UELib.Core
{
    /// <summary>
    /// Represents a unreal property.
    /// </summary>
    public partial class UProperty : UField, IUnrealNetObject
    {
        #region PreInitialized Members

        public PropertyType Type { get; protected set; }

        #endregion

        #region Serialized Members

        public int ArrayDim;
        public ushort ElementSize { get; private set; }

        public UnrealFlags<PropertyFlag> PropertyFlags;

#if XCOM2
        public UName? ConfigName;
#endif

        public UName? CategoryName;

        [Obsolete("See CategoryName")] public int CategoryIndex { get; }

        public UEnum? ArrayEnum;

        public UName? RepNotifyFuncName;
        public ushort RepOffset { get; set; }
        public bool RepReliable => HasPropertyFlag(PropertyFlagsLO.Net);
        public uint RepKey => RepOffset | ((uint)Convert.ToByte(RepReliable) << 16);

        /// <summary>
        /// Stored meta-data in the "option" format (i.e. WebAdmin, and commandline options), used to assist developers in the editor.
        /// e.g. <code>var int MyVariable "PI:Property Two:Game:1:60:Check" ...["SecondOption"]</code>
        /// 
        /// An original terminating \" character is serialized as a \n character, the string will also end with a newline character.
        /// </summary>
        public string? EditorDataText;

        #endregion

        #region General Members

        private bool _IsArray => ArrayDim > 1;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new instance of the UELib.Core.UProperty class.
        /// </summary>
        public UProperty()
        {
            Type = PropertyType.None;
        }

        protected override void Deserialize()
        {
#if ADVENT
            if (_Buffer.Package.Build == UnrealPackage.GameBuild.BuildName.Advent)
            {
                // Serialize FProperty

                ArrayDim = _Buffer.ReadInt32();
                Record(nameof(ArrayDim), ArrayDim);

                ArrayDim = PropertyFlags >> 16;
                uint propertyIndex = PropertyFlags & 0x0000FFFFU;

                PropertyFlags = new UnrealFlags<PropertyFlag>(_Buffer.ReadUInt32(), _Buffer.Package.Branch.EnumFlagsMap[typeof(PropertyFlag)]);
                Record(nameof(PropertyFlags), PropertyFlags);

                CategoryName = _Buffer.ReadNameReference();
                Record(nameof(CategoryName), CategoryName);

                if (_Buffer.LicenseeVersion < 6 && (PropertyFlags & 0x20) != 0)
                {
                    RepOffset = _Buffer.ReadUInt16();
                    Record(nameof(RepOffset), RepOffset);
                }

                // Skip base.
                return;
            }
#endif
            base.Deserialize();
#if SPLINTERCELLX
            if (Package.Build == BuildGeneration.SCX &&
                _Buffer.LicenseeVersion >= 15)
            {
                // 32bit => 16bit
                ArrayDim = _Buffer.ReadUInt16();
                Record(nameof(ArrayDim), ArrayDim);

                _Buffer.Read(out PropertyFlags);
                Record(nameof(PropertyFlags), PropertyFlags);

                _Buffer.Read(out CategoryName);
                Record(nameof(CategoryName), CategoryName);

                // FIXME: Unknown version, attested without a version check since SC3 and SC4.
                if (_Buffer.LicenseeVersion > 17) // 17 = newer than SC1
                {
                    // Music? Some kind of alternative to category name
                    _Buffer.Read(out UName v68);
                    Record(nameof(v68), v68);
                }

                return;
            }
#endif
#if LEAD
            if (Package.Build == BuildGeneration.Lead)
            {
                // 32bit => 16bit
                ArrayDim = _Buffer.ReadUInt16();
                Record(nameof(ArrayDim), ArrayDim);

                _Buffer.Read(out PropertyFlags);
                Record(nameof(PropertyFlags), PropertyFlags);

                if (_Buffer.LicenseeVersion >= 72)
                {
                    ushort v34 = _Buffer.ReadUInt16();
                    Record(nameof(v34), v34);
                }

                _Buffer.Read(out CategoryName);
                Record(nameof(CategoryName), CategoryName);

                // not versioned
                var v4c = _Buffer.ReadNameReference();
                Record(nameof(v4c), v4c);

                if (_Buffer.LicenseeVersion >= 4)
                {
                    // CommentString
                    EditorDataText = _Buffer.ReadString(); // v50
                    Record(nameof(EditorDataText), EditorDataText);
                }

                if (_Buffer.LicenseeVersion >= 11)
                {
                    // Usually 0 or 0xAA88FF
                    uint v5c = _Buffer.ReadUInt32();
                    Record(nameof(v5c), v5c);

                    uint v60 = _Buffer.ReadUInt32();
                    Record(nameof(v60), v60);

                    // Display name e.g. SpecularMask = Specular
                    string v64 = _Buffer.ReadString();
                    Record(nameof(v64), v64);
                }

                if (_Buffer.LicenseeVersion >= 101)
                {
                    var v7c = _Buffer.ReadNameReference();
                    Record(nameof(v7c), v7c);
                }

                return;
            }
#endif
#if SWRepublicCommando
            if (Package.Build == UnrealPackage.GameBuild.BuildName.SWRepublicCommando)
            {
                if (_Buffer.Version < 137)
                {
                    NextField = _Buffer.ReadObject<UField>();
                    Record(nameof(NextField), NextField);
                }

                if (_Buffer.Version >= 136)
                {
                    // 32bit => 16bit
                    ArrayDim = _Buffer.ReadUInt16();
                    Record(nameof(ArrayDim), ArrayDim);

                    goto skipArrayDim;
                }
            }
#endif
#if AA2
            if (Package.Build == BuildGeneration.AGP &&
                _Buffer.LicenseeVersion >= 8)
            {
                // Always 26125 (hardcoded in the assembly) 
                uint aa2FixedPack = _Buffer.ReadUInt32();
                Record(nameof(aa2FixedPack), aa2FixedPack);
            }
#endif
#if XIII || DNF || MOV
            // TODO: (UE2X) Version 131 ArrayDim size changed from DWORD to WORD
            if (Package.Build == UnrealPackage.GameBuild.BuildName.XIII ||
                Package.Build == UnrealPackage.GameBuild.BuildName.DNF ||
                Package.Build == UnrealPackage.GameBuild.BuildName.MOV)
            {
                ArrayDim = _Buffer.ReadInt16();
                Record(nameof(ArrayDim), ArrayDim);

                goto skipArrayDim;
            }
#endif
            ArrayDim = _Buffer.ReadInt32();
            Record(nameof(ArrayDim), ArrayDim);
            ElementSize = (ushort)(ArrayDim >> 16);
        skipArrayDim:
            // Just to verify if this is in use at all.
            //Debug.Assert(ElementSize == 0, $"ElementSize: {ElementSize}");
            // 2048 is the max allowed dimension in the UnrealScript compiler, however some licensees have extended this to a much higher size.
            //Debug.Assert(
            //    (ArrayDim & 0x0000FFFFU) > 0 && (ArrayDim & 0x0000FFFFU) <= 2048, 
            //    $"Bad array dimension {ArrayDim & 0x0000FFFFU} for property ${GetReferencePath()}");

            var propertyFlags = Package.Version >= (uint)PackageObjectLegacyVersion.PropertyFlagsSizeExpandedTo64Bits
                ? _Buffer.ReadUInt64()
                : _Buffer.ReadUInt32();
#if BATMAN
            if (Package.Build == BuildGeneration.RSS)
            {
                if (_Buffer.LicenseeVersion >= 101)
                {
                    // DAT_14313fdc0
                    ulong[] flagMasks =
                    [
                        0x0000000000000002, 0x0000000000000004, 0x0000000000000008, 0x0000000000000080,
                        0x0000000000000010, 0x0000000000000100, 0x0000000000000200, 0x0000000000000400,
                        0x0000000000000800, 0x0000000000001000, 0x0000000000002000, 0x0000000000004000,
                        0x0000000000008000, 0x0000000000040000, 0x0000000000080000, 0x0000000000200000,
                        0x0000000000400000, 0x0000000000800000, 0x0000000010000000, 0x0000000200000000,
                        0x0000000400000000, 0x0000000800000000, 0x0000004000000000, 0x0000010000000000,
                        0x0000020000000000, 0x0000000000000020, 0x0000000200000000, 0x0000000010000000,
                        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000001,
                        0x0000000000000040, 0x0000000000020000, 0x0000000000200000, 0x0000004000000000,
                        0x0000008000000000, 0x0000000040000000, 0x0000000100000000, 0x0000002000000000,
                        0x0000010000000000, 0x0000001000000000, 0x0000000080000000, 0x0000000100000000,
                        0x0000000400000000, 0x0000000800000000, 0x0000000000000000
                    ];

                    ulong originalFlags = 0;
                    ulong bitMask = 1;

                    foreach (ulong flag in flagMasks)
                    {
                        if ((propertyFlags & bitMask) != 0)
                        {
                            originalFlags |= flag;
                        }

                        bitMask <<= 1;
                    }

                    propertyFlags = originalFlags;
                }
            }
#endif
            PropertyFlags = new UnrealFlags<PropertyFlag>(propertyFlags, _Buffer.Package.Branch.EnumFlagsMap[typeof(PropertyFlag)]);
            Record(nameof(PropertyFlags), PropertyFlags);
#if XCOM2
            if (Package.Build == UnrealPackage.GameBuild.BuildName.XCOM2WotC)
            {
                ConfigName = _Buffer.ReadNameReference();
                Record(nameof(ConfigName), ConfigName);
            }
#endif
#if THIEF_DS || DEUSEX_IW
            if (Package.Build == BuildGeneration.Flesh)
            {
                // Property flags like CustomEditor, CustomViewer, ThiefProp, DeusExProp, NoTextExport, NoTravel
                uint deusFlags = _Buffer.ReadUInt32();
                Record(nameof(deusFlags), deusFlags);
            }
#endif
            if (!Package.IsConsoleCooked()
#if MASS_EFFECT
                // M1:LE is cooked for "WindowsConsole" yet retains this data.
                || Package.Build == BuildGeneration.SFX
#endif
               )
            {
                // TODO: Not serialized if XENON (UE2X)
                // FIXME: UE4 version
                if (_Buffer.UE4Version < 160)
                {
                    CategoryName = _Buffer.ReadNameReference();
                    Record(nameof(CategoryName), CategoryName);
                }

                if (_Buffer.Version >= (uint)PackageObjectLegacyVersion.AddedArrayEnumToUProperty
#if MIDWAY
                    || Package.Build == UnrealPackage.GameBuild.BuildName.Stranglehold
#endif
                    )
                {
                    ArrayEnum = _Buffer.ReadObject<UEnum>();
                    Record(nameof(ArrayEnum), ArrayEnum);
                }
            }

#if THIEF_DS || DEUSEX_IW
            if (Package.Build == BuildGeneration.Flesh)
            {
                short deusInheritedOrRuntimeInstantiated = _Buffer.ReadInt16();
                Record(nameof(deusInheritedOrRuntimeInstantiated), deusInheritedOrRuntimeInstantiated);
                short deusUnkInt16 = _Buffer.ReadInt16();
                Record(nameof(deusUnkInt16), deusUnkInt16);
            }
#endif
#if BORDERLANDS
            if (Package.Build == BuildGeneration.GB &&
                _Buffer.LicenseeVersion >= 2)
            {
                var va8 = _Buffer.ReadObject();
                Record(nameof(va8), va8);
                var vb0 = _Buffer.ReadObject();
                Record(nameof(vb0), vb0);
            }
#endif
#if UE4
            if (_Buffer.UE4Version > 0)
            {
                RepNotifyFuncName = _Buffer.ReadNameReference();
                Record(nameof(RepNotifyFuncName), RepNotifyFuncName);

                return;
            }
#endif
            if (PropertyFlags.HasFlag(PropertyFlag.Net))
            {
                RepOffset = _Buffer.ReadUShort();
                Record(nameof(RepOffset), RepOffset);
            }
#if HUXLEY
            if (Package.Build == UnrealPackage.GameBuild.BuildName.Huxley)
            {
                // A property linked to the "Core.Object.LazyLoadPropertyInfo" struct.
                var partLoadInfoProperty = _Buffer.ReadObject();
                Record(nameof(partLoadInfoProperty), partLoadInfoProperty);
            }
#endif
#if R6
            if (Package.Build == UnrealPackage.GameBuild.BuildName.R6Vegas)
            {
                _Buffer.Read(out string v0c);
                Record(nameof(v0c), v0c);

                EditorDataText = v0c;
            }
#endif
#if ROCKETLEAGUE
            // identical to this object's name.
            if (_Buffer.Package.Build == UnrealPackage.GameBuild.BuildName.RocketLeague &&
                _Buffer.LicenseeVersion >= 11)
            {
                string vb8 = _Buffer.ReadString();
                Record(nameof(vb8), vb8);

                //if (_Buffer.LicenseeVersion == 15)
                //{
                //    var v68 = _Buffer.ReadNameReference();
                //    Record(nameof(v68), v68);
                //}
            }
#endif
#if VENGEANCE
            if (Package.Build == BuildGeneration.Vengeance)
            {
                var vengeanceEditComboType = _Buffer.ReadNameReference();
                Record(nameof(vengeanceEditComboType), vengeanceEditComboType);
                var vengeanceEditDisplay = _Buffer.ReadNameReference();
                Record(nameof(vengeanceEditDisplay), vengeanceEditDisplay);
            }
#endif
#if DNF
            if (Package.Build == UnrealPackage.GameBuild.BuildName.DNF)
            {
                if (HasPropertyFlag(0x800000))
                {
                    EditorDataText = _Buffer.ReadString();
                    Record(nameof(EditorDataText), EditorDataText);
                }

                // Same flag as EditorData, but this may merely be a coincidence, see above
                if (_Buffer.Version >= 118 && HasPropertyFlag(0x2000000))
                {
                    // a.k.a NetUpdateName ;)
                    RepNotifyFuncName = _Buffer.ReadNameReference();
                    Record(nameof(RepNotifyFuncName), RepNotifyFuncName);
                }

                return;
            }
#endif
            // Appears to be a UE2.5 feature, it is not present in UE2 builds with no custom LicenseeVersion
            // Albeit DeusEx indicates otherwise?
            if ((PropertyFlags.HasFlag(PropertyFlag.CommentString) &&
                 (Package.Build == BuildGeneration.UE2_5
                  || Package.Build == BuildGeneration.AGP
                  || Package.Build == BuildGeneration.Flesh))
                // No property flag check
#if VENGEANCE
                || Package.Build == BuildGeneration.Vengeance
#endif
#if MOV
                // No property flag check
                || Package.Build == UnrealPackage.GameBuild.BuildName.MOV
#endif
#if LSGAME
                // No property flag check
                || (Package.Build == UnrealPackage.GameBuild.BuildName.LSGame &&
                    Package.LicenseeVersion >= 3)
#endif
#if DEVASTATION
                // No property flag check
                || Package.Build == UnrealPackage.GameBuild.BuildName.Devastation
#endif
               )
            {
                // May represent a tooltip/comment in some games. Usually in the form of a quoted string, sometimes as a double-flash comment or both.
                EditorDataText = _Buffer.ReadString();
                Record(nameof(EditorDataText), EditorDataText);
            }
#if SPELLBORN
            if (Package.Build == UnrealPackage.GameBuild.BuildName.Spellborn)
            {
                if (_Buffer.Version < 157)
                {
                    throw new NotSupportedException("< 157 Spellborn packages are not supported");

                    if (133 < _Buffer.Version)
                    {
                        // idk
                    }

                    if (134 < _Buffer.Version)
                    {
                        int unk32 = _Buffer.ReadInt32();
                        Record("Unknown:Spellborn", unk32);
                    }
                }
                else
                {
                    uint replicationFlags = _Buffer.ReadUInt32();
                    Record(nameof(replicationFlags), replicationFlags);
                }
            }
#endif
        }

        protected override bool CanDisposeBuffer()
        {
            return true;
        }

        #endregion

        #region Methods

        [Obsolete("Use PropertyFlags directly")]
        public bool HasPropertyFlag(uint flag)
        {
            return ((uint)PropertyFlags & flag) != 0;
        }

        [Obsolete("Use PropertyFlags directly")]
        public bool HasPropertyFlag(PropertyFlagsLO flag)
        {
            return ((uint)(PropertyFlags & 0x00000000FFFFFFFFU) & (uint)flag) != 0;
        }

        [Obsolete("Use PropertyFlags directly")]
        public bool HasPropertyFlag(PropertyFlagsHO flag)
        {
            return (PropertyFlags & ((ulong)flag << 32)) != 0;
        }

        internal bool HasPropertyFlag(PropertyFlag flagIndex)
        {
            return PropertyFlags.HasFlag(Package.Branch.EnumFlagsMap[typeof(PropertyFlag)], flagIndex);
        }

        public bool IsParm()
        {
            return PropertyFlags.HasFlag(PropertyFlag.Parm);
        }

        public virtual string GetFriendlyInnerType()
        {
            return string.Empty;
        }

        #endregion
    }
}
