class DefaultProperties extends DefaultPropertiesBase;

// Primitives
var byte Byte;
var int Int;
// var bool Bool;
var float Float;
var name NameProperty;
// var string[255] String;
var string String;

// var delegate<OnDelegate> Delegate;
var Object Object;
var Class<DefaultProperties> MetaClass;

// FixedArray
var byte ByteFixedArray[2];
var int IntFixedArray[2];

// Structs
var Guid Guid;
var Vector Vector;
var Plane Plane;
var Rotator Rotator;
var Coords Coords;
var Scale Scale;
var Color Color;

var array<byte> ByteArray;

var enum EEnum
{
	EEnum1,
	EEnum2,
	EEnum3
} Enum;

var struct sStruct
{
	// Test all unique types in a struct
	var byte Byte;
	var int Int;
	var float Float;
	var name NameProperty;
	var string String;
	var Object Object;
	var Class<DefaultProperties> MetaClass;
	var byte ByteFixedArray[2];
	var Object.Vector Vector;
	var array<byte> ByteArray;
	var EEnum Enum;

	// Test struct within struct.
	var struct sStruct2
	{
		var byte Byte;
	} Struct2;
} Struct;

defaultproperties
{
    BoolTrue=true
    BoolFalse=false

    Byte=255
    Int=1000
    Float=.0123456789
    NameProperty="Name"
    // ASCII
    String="String_\""

    Object=Object'DefaultProperties'
    MetaClass=Class'DefaultProperties'

    ByteFixedArray(0)=1
    IntFixedArray(0)=1
    IntFixedArray(1)=2

	Vector=(X=1.0,Y=2.0,Z=3.0)
	Plane=(W=0.0,X=1.0,Y=2.0,Z=3.0)
	Rotator=(Pitch=180,Yaw=90,Roll=45)
	Coords=(Origin=(X=0.2,Y=0.4,Z=1.0),XAxis=(X=1.0,Y=0.0,Z=0.0),YAxis=(X=0.0,Y=1.0,Z=0.0),ZAxis=(X=0.0,Y=0.0,Z=1.0))
	Scale=(Scale=(X=1.0,Y=2.0,Z=3.0),SheerRate=5.0,SheerAxis=SHEER_ZY)
	Color=(B=20,G=40,R=80,A=160)

    ByteArray(0)=1
    ByteArray(2)=1

    BaseByteArray(2)=2

	Enum=1
	Struct=(Struct2=(Byte=1),Byte=1,Int=1,Float=1.0,NameProperty=Name,String="String",Object=Class'DefaultProperties',MetaClass=Class'DefaultProperties',Vector=(X=1.0,Y=2.0,Z=3.0),Enum=EEnum2)
}
