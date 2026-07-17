; ModuleID = 'marshal_methods.armeabi-v7a.ll'
source_filename = "marshal_methods.armeabi-v7a.ll"
target datalayout = "e-m:e-p:32:32-Fi8-i64:64-v128:64:128-a:0:32-n32-S64"
target triple = "armv7-unknown-linux-android21"

%struct.MarshalMethodName = type {
	i64, ; uint64_t id
	ptr ; char* name
}

%struct.MarshalMethodsManagedClass = type {
	i32, ; uint32_t token
	ptr ; MonoClass klass
}

@assembly_image_cache = dso_local local_unnamed_addr global [123 x ptr] zeroinitializer, align 4

; Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array
@assembly_image_cache_hashes = dso_local local_unnamed_addr constant [246 x i32] [
	i32 10166715, ; 0: System.Net.NameResolution.dll => 0x9b21bb => 98
	i32 42639949, ; 1: System.Threading.Thread => 0x28aa24d => 114
	i32 67008169, ; 2: zh-Hant\Microsoft.Maui.Controls.resources => 0x3fe76a9 => 33
	i32 72070932, ; 3: Microsoft.Maui.Graphics.dll => 0x44bb714 => 49
	i32 117431740, ; 4: System.Runtime.InteropServices => 0x6ffddbc => 106
	i32 165246403, ; 5: Xamarin.AndroidX.Collection.dll => 0x9d975c3 => 54
	i32 182336117, ; 6: Xamarin.AndroidX.SwipeRefreshLayout.dll => 0xade3a75 => 73
	i32 195452805, ; 7: vi/Microsoft.Maui.Controls.resources.dll => 0xba65f85 => 30
	i32 199333315, ; 8: zh-HK/Microsoft.Maui.Controls.resources.dll => 0xbe195c3 => 31
	i32 205061960, ; 9: System.ComponentModel => 0xc38ff48 => 87
	i32 280992041, ; 10: cs/Microsoft.Maui.Controls.resources.dll => 0x10bf9929 => 2
	i32 317674968, ; 11: vi\Microsoft.Maui.Controls.resources => 0x12ef55d8 => 30
	i32 318968648, ; 12: Xamarin.AndroidX.Activity.dll => 0x13031348 => 50
	i32 336156722, ; 13: ja/Microsoft.Maui.Controls.resources.dll => 0x14095832 => 15
	i32 342366114, ; 14: Xamarin.AndroidX.Lifecycle.Common => 0x146817a2 => 61
	i32 356389973, ; 15: it/Microsoft.Maui.Controls.resources.dll => 0x153e1455 => 14
	i32 379916513, ; 16: System.Threading.Thread.dll => 0x16a510e1 => 114
	i32 385762202, ; 17: System.Memory.dll => 0x16fe439a => 96
	i32 395744057, ; 18: _Microsoft.Android.Resource.Designer => 0x17969339 => 34
	i32 435591531, ; 19: sv/Microsoft.Maui.Controls.resources.dll => 0x19f6996b => 26
	i32 442565967, ; 20: System.Collections => 0x1a61054f => 84
	i32 450948140, ; 21: Xamarin.AndroidX.Fragment.dll => 0x1ae0ec2c => 60
	i32 456227837, ; 22: System.Web.HttpUtility.dll => 0x1b317bfd => 116
	i32 469710990, ; 23: System.dll => 0x1bff388e => 118
	i32 498788369, ; 24: System.ObjectModel => 0x1dbae811 => 103
	i32 500358224, ; 25: id/Microsoft.Maui.Controls.resources.dll => 0x1dd2dc50 => 13
	i32 503918385, ; 26: fi/Microsoft.Maui.Controls.resources.dll => 0x1e092f31 => 7
	i32 513247710, ; 27: Microsoft.Extensions.Primitives.dll => 0x1e9789de => 44
	i32 539058512, ; 28: Microsoft.Extensions.Logging => 0x20216150 => 41
	i32 592146354, ; 29: pt-BR/Microsoft.Maui.Controls.resources.dll => 0x234b6fb2 => 21
	i32 597488923, ; 30: CommunityToolkit.Maui => 0x239cf51b => 35
	i32 627609679, ; 31: Xamarin.AndroidX.CustomView => 0x2568904f => 58
	i32 627931235, ; 32: nl\Microsoft.Maui.Controls.resources => 0x256d7863 => 19
	i32 662205335, ; 33: System.Text.Encodings.Web.dll => 0x27787397 => 111
	i32 672442732, ; 34: System.Collections.Concurrent => 0x2814a96c => 81
	i32 688181140, ; 35: ca/Microsoft.Maui.Controls.resources.dll => 0x2904cf94 => 1
	i32 706645707, ; 36: ko/Microsoft.Maui.Controls.resources.dll => 0x2a1e8ecb => 16
	i32 709557578, ; 37: de/Microsoft.Maui.Controls.resources.dll => 0x2a4afd4a => 4
	i32 722857257, ; 38: System.Runtime.Loader.dll => 0x2b15ed29 => 107
	i32 759454413, ; 39: System.Net.Requests => 0x2d445acd => 100
	i32 775507847, ; 40: System.IO.Compression => 0x2e394f87 => 93
	i32 777317022, ; 41: sk\Microsoft.Maui.Controls.resources => 0x2e54ea9e => 25
	i32 789151979, ; 42: Microsoft.Extensions.Options => 0x2f0980eb => 43
	i32 823281589, ; 43: System.Private.Uri.dll => 0x311247b5 => 104
	i32 830298997, ; 44: System.IO.Compression.Brotli => 0x317d5b75 => 92
	i32 904024072, ; 45: System.ComponentModel.Primitives.dll => 0x35e25008 => 85
	i32 926902833, ; 46: tr/Microsoft.Maui.Controls.resources.dll => 0x373f6a31 => 28
	i32 966729478, ; 47: Xamarin.Google.Crypto.Tink.Android => 0x399f1f06 => 77
	i32 967690846, ; 48: Xamarin.AndroidX.Lifecycle.Common.dll => 0x39adca5e => 61
	i32 992768348, ; 49: System.Collections.dll => 0x3b2c715c => 84
	i32 1012816738, ; 50: Xamarin.AndroidX.SavedState.dll => 0x3c5e5b62 => 71
	i32 1028951442, ; 51: Microsoft.Extensions.DependencyInjection.Abstractions => 0x3d548d92 => 40
	i32 1029334545, ; 52: da/Microsoft.Maui.Controls.resources.dll => 0x3d5a6611 => 3
	i32 1035644815, ; 53: Xamarin.AndroidX.AppCompat => 0x3dbaaf8f => 51
	i32 1044663988, ; 54: System.Linq.Expressions.dll => 0x3e444eb4 => 94
	i32 1052210849, ; 55: Xamarin.AndroidX.Lifecycle.ViewModel.dll => 0x3eb776a1 => 63
	i32 1082857460, ; 56: System.ComponentModel.TypeConverter => 0x408b17f4 => 86
	i32 1084122840, ; 57: Xamarin.Kotlin.StdLib => 0x409e66d8 => 78
	i32 1098259244, ; 58: System => 0x41761b2c => 118
	i32 1118262833, ; 59: ko\Microsoft.Maui.Controls.resources => 0x42a75631 => 16
	i32 1168523401, ; 60: pt\Microsoft.Maui.Controls.resources => 0x45a64089 => 22
	i32 1178241025, ; 61: Xamarin.AndroidX.Navigation.Runtime.dll => 0x463a8801 => 68
	i32 1203215381, ; 62: pl/Microsoft.Maui.Controls.resources.dll => 0x47b79c15 => 20
	i32 1234928153, ; 63: nb/Microsoft.Maui.Controls.resources.dll => 0x499b8219 => 18
	i32 1260983243, ; 64: cs\Microsoft.Maui.Controls.resources => 0x4b2913cb => 2
	i32 1293217323, ; 65: Xamarin.AndroidX.DrawerLayout.dll => 0x4d14ee2b => 59
	i32 1324164729, ; 66: System.Linq => 0x4eed2679 => 95
	i32 1373134921, ; 67: zh-Hans\Microsoft.Maui.Controls.resources => 0x51d86049 => 32
	i32 1376866003, ; 68: Xamarin.AndroidX.SavedState => 0x52114ed3 => 71
	i32 1406073936, ; 69: Xamarin.AndroidX.CoordinatorLayout => 0x53cefc50 => 55
	i32 1430672901, ; 70: ar\Microsoft.Maui.Controls.resources => 0x55465605 => 0
	i32 1435222561, ; 71: Xamarin.Google.Crypto.Tink.Android.dll => 0x558bc221 => 77
	i32 1452070440, ; 72: System.Formats.Asn1.dll => 0x568cd628 => 91
	i32 1461004990, ; 73: es\Microsoft.Maui.Controls.resources => 0x57152abe => 6
	i32 1461234159, ; 74: System.Collections.Immutable.dll => 0x5718a9ef => 82
	i32 1462112819, ; 75: System.IO.Compression.dll => 0x57261233 => 93
	i32 1469204771, ; 76: Xamarin.AndroidX.AppCompat.AppCompatResources => 0x57924923 => 52
	i32 1470490898, ; 77: Microsoft.Extensions.Primitives => 0x57a5e912 => 44
	i32 1479771757, ; 78: System.Collections.Immutable => 0x5833866d => 82
	i32 1480492111, ; 79: System.IO.Compression.Brotli.dll => 0x583e844f => 92
	i32 1493001747, ; 80: hi/Microsoft.Maui.Controls.resources.dll => 0x58fd6613 => 10
	i32 1514721132, ; 81: el/Microsoft.Maui.Controls.resources.dll => 0x5a48cf6c => 5
	i32 1543031311, ; 82: System.Text.RegularExpressions.dll => 0x5bf8ca0f => 113
	i32 1551623176, ; 83: sk/Microsoft.Maui.Controls.resources.dll => 0x5c7be408 => 25
	i32 1622152042, ; 84: Xamarin.AndroidX.Loader.dll => 0x60b0136a => 65
	i32 1624863272, ; 85: Xamarin.AndroidX.ViewPager2 => 0x60d97228 => 75
	i32 1634654947, ; 86: CommunityToolkit.Maui.Core.dll => 0x616edae3 => 36
	i32 1636350590, ; 87: Xamarin.AndroidX.CursorAdapter => 0x6188ba7e => 57
	i32 1639515021, ; 88: System.Net.Http.dll => 0x61b9038d => 97
	i32 1639986890, ; 89: System.Text.RegularExpressions => 0x61c036ca => 113
	i32 1657153582, ; 90: System.Runtime => 0x62c6282e => 109
	i32 1658251792, ; 91: Xamarin.Google.Android.Material.dll => 0x62d6ea10 => 76
	i32 1677501392, ; 92: System.Net.Primitives.dll => 0x63fca3d0 => 99
	i32 1679769178, ; 93: System.Security.Cryptography => 0x641f3e5a => 110
	i32 1729485958, ; 94: Xamarin.AndroidX.CardView.dll => 0x6715dc86 => 53
	i32 1736233607, ; 95: ro/Microsoft.Maui.Controls.resources.dll => 0x677cd287 => 23
	i32 1743415430, ; 96: ca\Microsoft.Maui.Controls.resources => 0x67ea6886 => 1
	i32 1763938596, ; 97: System.Diagnostics.TraceSource.dll => 0x69239124 => 90
	i32 1766324549, ; 98: Xamarin.AndroidX.SwipeRefreshLayout => 0x6947f945 => 73
	i32 1770582343, ; 99: Microsoft.Extensions.Logging.dll => 0x6988f147 => 41
	i32 1780572499, ; 100: Mono.Android.Runtime.dll => 0x6a216153 => 121
	i32 1782862114, ; 101: ms\Microsoft.Maui.Controls.resources => 0x6a445122 => 17
	i32 1788241197, ; 102: Xamarin.AndroidX.Fragment => 0x6a96652d => 60
	i32 1793755602, ; 103: he\Microsoft.Maui.Controls.resources => 0x6aea89d2 => 9
	i32 1808609942, ; 104: Xamarin.AndroidX.Loader => 0x6bcd3296 => 65
	i32 1813058853, ; 105: Xamarin.Kotlin.StdLib.dll => 0x6c111525 => 78
	i32 1813201214, ; 106: Xamarin.Google.Android.Material => 0x6c13413e => 76
	i32 1818569960, ; 107: Xamarin.AndroidX.Navigation.UI.dll => 0x6c652ce8 => 69
	i32 1828688058, ; 108: Microsoft.Extensions.Logging.Abstractions.dll => 0x6cff90ba => 42
	i32 1842015223, ; 109: uk/Microsoft.Maui.Controls.resources.dll => 0x6dcaebf7 => 29
	i32 1853025655, ; 110: sv\Microsoft.Maui.Controls.resources => 0x6e72ed77 => 26
	i32 1858542181, ; 111: System.Linq.Expressions => 0x6ec71a65 => 94
	i32 1875935024, ; 112: fr\Microsoft.Maui.Controls.resources => 0x6fd07f30 => 8
	i32 1910275211, ; 113: System.Collections.NonGeneric.dll => 0x71dc7c8b => 83
	i32 1961813231, ; 114: Xamarin.AndroidX.Security.SecurityCrypto.dll => 0x74eee4ef => 72
	i32 1968388702, ; 115: Microsoft.Extensions.Configuration.dll => 0x75533a5e => 37
	i32 2003115576, ; 116: el\Microsoft.Maui.Controls.resources => 0x77651e38 => 5
	i32 2019465201, ; 117: Xamarin.AndroidX.Lifecycle.ViewModel => 0x785e97f1 => 63
	i32 2025202353, ; 118: ar/Microsoft.Maui.Controls.resources.dll => 0x78b622b1 => 0
	i32 2045470958, ; 119: System.Private.Xml => 0x79eb68ee => 105
	i32 2055257422, ; 120: Xamarin.AndroidX.Lifecycle.LiveData.Core.dll => 0x7a80bd4e => 62
	i32 2066184531, ; 121: de\Microsoft.Maui.Controls.resources => 0x7b277953 => 4
	i32 2070888862, ; 122: System.Diagnostics.TraceSource => 0x7b6f419e => 90
	i32 2079903147, ; 123: System.Runtime.dll => 0x7bf8cdab => 109
	i32 2090596640, ; 124: System.Numerics.Vectors => 0x7c9bf920 => 102
	i32 2127167465, ; 125: System.Console => 0x7ec9ffe9 => 88
	i32 2159891885, ; 126: Microsoft.Maui => 0x80bd55ad => 47
	i32 2169148018, ; 127: hu\Microsoft.Maui.Controls.resources => 0x814a9272 => 12
	i32 2181898931, ; 128: Microsoft.Extensions.Options.dll => 0x820d22b3 => 43
	i32 2192057212, ; 129: Microsoft.Extensions.Logging.Abstractions => 0x82a8237c => 42
	i32 2193016926, ; 130: System.ObjectModel.dll => 0x82b6c85e => 103
	i32 2201107256, ; 131: Xamarin.KotlinX.Coroutines.Core.Jvm.dll => 0x83323b38 => 79
	i32 2201231467, ; 132: System.Net.Http => 0x8334206b => 97
	i32 2207618523, ; 133: it\Microsoft.Maui.Controls.resources => 0x839595db => 14
	i32 2228180035, ; 134: P2PFil => 0x84cf5443 => 80
	i32 2266799131, ; 135: Microsoft.Extensions.Configuration.Abstractions => 0x871c9c1b => 38
	i32 2270573516, ; 136: fr/Microsoft.Maui.Controls.resources.dll => 0x875633cc => 8
	i32 2279755925, ; 137: Xamarin.AndroidX.RecyclerView.dll => 0x87e25095 => 70
	i32 2295906218, ; 138: System.Net.Sockets => 0x88d8bfaa => 101
	i32 2303942373, ; 139: nb\Microsoft.Maui.Controls.resources => 0x89535ee5 => 18
	i32 2305521784, ; 140: System.Private.CoreLib.dll => 0x896b7878 => 119
	i32 2353062107, ; 141: System.Net.Primitives => 0x8c40e0db => 99
	i32 2368005991, ; 142: System.Xml.ReaderWriter.dll => 0x8d24e767 => 117
	i32 2371007202, ; 143: Microsoft.Extensions.Configuration => 0x8d52b2e2 => 37
	i32 2395872292, ; 144: id\Microsoft.Maui.Controls.resources => 0x8ece1c24 => 13
	i32 2401565422, ; 145: System.Web.HttpUtility => 0x8f24faee => 116
	i32 2427813419, ; 146: hi\Microsoft.Maui.Controls.resources => 0x90b57e2b => 10
	i32 2435356389, ; 147: System.Console.dll => 0x912896e5 => 88
	i32 2458678730, ; 148: System.Net.Sockets.dll => 0x928c75ca => 101
	i32 2475788418, ; 149: Java.Interop.dll => 0x93918882 => 120
	i32 2480646305, ; 150: Microsoft.Maui.Controls => 0x93dba8a1 => 45
	i32 2550873716, ; 151: hr\Microsoft.Maui.Controls.resources => 0x980b3e74 => 11
	i32 2570120770, ; 152: System.Text.Encodings.Web => 0x9930ee42 => 111
	i32 2593496499, ; 153: pl\Microsoft.Maui.Controls.resources => 0x9a959db3 => 20
	i32 2605712449, ; 154: Xamarin.KotlinX.Coroutines.Core.Jvm => 0x9b500441 => 79
	i32 2617129537, ; 155: System.Private.Xml.dll => 0x9bfe3a41 => 105
	i32 2620871830, ; 156: Xamarin.AndroidX.CursorAdapter.dll => 0x9c375496 => 57
	i32 2626831493, ; 157: ja\Microsoft.Maui.Controls.resources => 0x9c924485 => 15
	i32 2663698177, ; 158: System.Runtime.Loader => 0x9ec4cf01 => 107
	i32 2724373263, ; 159: System.Runtime.Numerics.dll => 0xa262a30f => 108
	i32 2732626843, ; 160: Xamarin.AndroidX.Activity => 0xa2e0939b => 50
	i32 2737747696, ; 161: Xamarin.AndroidX.AppCompat.AppCompatResources.dll => 0xa32eb6f0 => 52
	i32 2752995522, ; 162: pt-BR\Microsoft.Maui.Controls.resources => 0xa41760c2 => 21
	i32 2758225723, ; 163: Microsoft.Maui.Controls.Xaml => 0xa4672f3b => 46
	i32 2764765095, ; 164: Microsoft.Maui.dll => 0xa4caf7a7 => 47
	i32 2778768386, ; 165: Xamarin.AndroidX.ViewPager.dll => 0xa5a0a402 => 74
	i32 2785988530, ; 166: th\Microsoft.Maui.Controls.resources => 0xa60ecfb2 => 27
	i32 2801831435, ; 167: Microsoft.Maui.Graphics => 0xa7008e0b => 49
	i32 2806116107, ; 168: es/Microsoft.Maui.Controls.resources.dll => 0xa741ef0b => 6
	i32 2810250172, ; 169: Xamarin.AndroidX.CoordinatorLayout.dll => 0xa78103bc => 55
	i32 2831556043, ; 170: nl/Microsoft.Maui.Controls.resources.dll => 0xa8c61dcb => 19
	i32 2853208004, ; 171: Xamarin.AndroidX.ViewPager => 0xaa107fc4 => 74
	i32 2861189240, ; 172: Microsoft.Maui.Essentials => 0xaa8a4878 => 48
	i32 2868488919, ; 173: CommunityToolkit.Maui.Core => 0xaaf9aad7 => 36
	i32 2909740682, ; 174: System.Private.CoreLib => 0xad6f1e8a => 119
	i32 2916838712, ; 175: Xamarin.AndroidX.ViewPager2.dll => 0xaddb6d38 => 75
	i32 2919462931, ; 176: System.Numerics.Vectors.dll => 0xae037813 => 102
	i32 2959614098, ; 177: System.ComponentModel.dll => 0xb0682092 => 87
	i32 2978675010, ; 178: Xamarin.AndroidX.DrawerLayout => 0xb18af942 => 59
	i32 2987532451, ; 179: Xamarin.AndroidX.Security.SecurityCrypto => 0xb21220a3 => 72
	i32 3038032645, ; 180: _Microsoft.Android.Resource.Designer.dll => 0xb514b305 => 34
	i32 3057625584, ; 181: Xamarin.AndroidX.Navigation.Common => 0xb63fa9f0 => 66
	i32 3059408633, ; 182: Mono.Android.Runtime => 0xb65adef9 => 121
	i32 3059793426, ; 183: System.ComponentModel.Primitives => 0xb660be12 => 85
	i32 3077302341, ; 184: hu/Microsoft.Maui.Controls.resources.dll => 0xb76be845 => 12
	i32 3103600923, ; 185: System.Formats.Asn1 => 0xb8fd311b => 91
	i32 3178803400, ; 186: Xamarin.AndroidX.Navigation.Fragment.dll => 0xbd78b0c8 => 67
	i32 3220365878, ; 187: System.Threading => 0xbff2e236 => 115
	i32 3258312781, ; 188: Xamarin.AndroidX.CardView => 0xc235e84d => 53
	i32 3305363605, ; 189: fi\Microsoft.Maui.Controls.resources => 0xc503d895 => 7
	i32 3316684772, ; 190: System.Net.Requests.dll => 0xc5b097e4 => 100
	i32 3317135071, ; 191: Xamarin.AndroidX.CustomView.dll => 0xc5b776df => 58
	i32 3346324047, ; 192: Xamarin.AndroidX.Navigation.Runtime => 0xc774da4f => 68
	i32 3357674450, ; 193: ru\Microsoft.Maui.Controls.resources => 0xc8220bd2 => 24
	i32 3358260929, ; 194: System.Text.Json => 0xc82afec1 => 112
	i32 3362522851, ; 195: Xamarin.AndroidX.Core => 0xc86c06e3 => 56
	i32 3366347497, ; 196: Java.Interop => 0xc8a662e9 => 120
	i32 3374999561, ; 197: Xamarin.AndroidX.RecyclerView => 0xc92a6809 => 70
	i32 3380005837, ; 198: P2PFil.dll => 0xc976cbcd => 80
	i32 3381016424, ; 199: da\Microsoft.Maui.Controls.resources => 0xc9863768 => 3
	i32 3428513518, ; 200: Microsoft.Extensions.DependencyInjection.dll => 0xcc5af6ee => 39
	i32 3463511458, ; 201: hr/Microsoft.Maui.Controls.resources.dll => 0xce70fda2 => 11
	i32 3471940407, ; 202: System.ComponentModel.TypeConverter.dll => 0xcef19b37 => 86
	i32 3476120550, ; 203: Mono.Android => 0xcf3163e6 => 122
	i32 3479583265, ; 204: ru/Microsoft.Maui.Controls.resources.dll => 0xcf663a21 => 24
	i32 3484440000, ; 205: ro\Microsoft.Maui.Controls.resources => 0xcfb055c0 => 23
	i32 3485117614, ; 206: System.Text.Json.dll => 0xcfbaacae => 112
	i32 3580758918, ; 207: zh-HK\Microsoft.Maui.Controls.resources => 0xd56e0b86 => 31
	i32 3608519521, ; 208: System.Linq.dll => 0xd715a361 => 95
	i32 3641597786, ; 209: Xamarin.AndroidX.Lifecycle.LiveData.Core => 0xd90e5f5a => 62
	i32 3643446276, ; 210: tr\Microsoft.Maui.Controls.resources => 0xd92a9404 => 28
	i32 3643854240, ; 211: Xamarin.AndroidX.Navigation.Fragment => 0xd930cda0 => 67
	i32 3657292374, ; 212: Microsoft.Extensions.Configuration.Abstractions.dll => 0xd9fdda56 => 38
	i32 3672681054, ; 213: Mono.Android.dll => 0xdae8aa5e => 122
	i32 3697841164, ; 214: zh-Hant/Microsoft.Maui.Controls.resources.dll => 0xdc68940c => 33
	i32 3724971120, ; 215: Xamarin.AndroidX.Navigation.Common.dll => 0xde068c70 => 66
	i32 3732100267, ; 216: System.Net.NameResolution => 0xde7354ab => 98
	i32 3748608112, ; 217: System.Diagnostics.DiagnosticSource => 0xdf6f3870 => 89
	i32 3786282454, ; 218: Xamarin.AndroidX.Collection => 0xe1ae15d6 => 54
	i32 3792276235, ; 219: System.Collections.NonGeneric => 0xe2098b0b => 83
	i32 3817368567, ; 220: CommunityToolkit.Maui.dll => 0xe3886bf7 => 35
	i32 3823082795, ; 221: System.Security.Cryptography.dll => 0xe3df9d2b => 110
	i32 3841636137, ; 222: Microsoft.Extensions.DependencyInjection.Abstractions.dll => 0xe4fab729 => 40
	i32 3849253459, ; 223: System.Runtime.InteropServices.dll => 0xe56ef253 => 106
	i32 3889960447, ; 224: zh-Hans/Microsoft.Maui.Controls.resources.dll => 0xe7dc15ff => 32
	i32 3896106733, ; 225: System.Collections.Concurrent.dll => 0xe839deed => 81
	i32 3896760992, ; 226: Xamarin.AndroidX.Core.dll => 0xe843daa0 => 56
	i32 3928044579, ; 227: System.Xml.ReaderWriter => 0xea213423 => 117
	i32 3931092270, ; 228: Xamarin.AndroidX.Navigation.UI => 0xea4fb52e => 69
	i32 3955647286, ; 229: Xamarin.AndroidX.AppCompat.dll => 0xebc66336 => 51
	i32 3980434154, ; 230: th/Microsoft.Maui.Controls.resources.dll => 0xed409aea => 27
	i32 3987592930, ; 231: he/Microsoft.Maui.Controls.resources.dll => 0xedadd6e2 => 9
	i32 4025784931, ; 232: System.Memory => 0xeff49a63 => 96
	i32 4046471985, ; 233: Microsoft.Maui.Controls.Xaml.dll => 0xf1304331 => 46
	i32 4073602200, ; 234: System.Threading.dll => 0xf2ce3c98 => 115
	i32 4094352644, ; 235: Microsoft.Maui.Essentials.dll => 0xf40add04 => 48
	i32 4100113165, ; 236: System.Private.Uri => 0xf462c30d => 104
	i32 4102112229, ; 237: pt/Microsoft.Maui.Controls.resources.dll => 0xf48143e5 => 22
	i32 4125707920, ; 238: ms/Microsoft.Maui.Controls.resources.dll => 0xf5e94e90 => 17
	i32 4126470640, ; 239: Microsoft.Extensions.DependencyInjection => 0xf5f4f1f0 => 39
	i32 4150914736, ; 240: uk\Microsoft.Maui.Controls.resources => 0xf769eeb0 => 29
	i32 4182413190, ; 241: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll => 0xf94a8f86 => 64
	i32 4213026141, ; 242: System.Diagnostics.DiagnosticSource.dll => 0xfb1dad5d => 89
	i32 4271975918, ; 243: Microsoft.Maui.Controls.dll => 0xfea12dee => 45
	i32 4274976490, ; 244: System.Runtime.Numerics => 0xfecef6ea => 108
	i32 4292120959 ; 245: Xamarin.AndroidX.Lifecycle.ViewModelSavedState => 0xffd4917f => 64
], align 4

@assembly_image_cache_indices = dso_local local_unnamed_addr constant [246 x i32] [
	i32 98, ; 0
	i32 114, ; 1
	i32 33, ; 2
	i32 49, ; 3
	i32 106, ; 4
	i32 54, ; 5
	i32 73, ; 6
	i32 30, ; 7
	i32 31, ; 8
	i32 87, ; 9
	i32 2, ; 10
	i32 30, ; 11
	i32 50, ; 12
	i32 15, ; 13
	i32 61, ; 14
	i32 14, ; 15
	i32 114, ; 16
	i32 96, ; 17
	i32 34, ; 18
	i32 26, ; 19
	i32 84, ; 20
	i32 60, ; 21
	i32 116, ; 22
	i32 118, ; 23
	i32 103, ; 24
	i32 13, ; 25
	i32 7, ; 26
	i32 44, ; 27
	i32 41, ; 28
	i32 21, ; 29
	i32 35, ; 30
	i32 58, ; 31
	i32 19, ; 32
	i32 111, ; 33
	i32 81, ; 34
	i32 1, ; 35
	i32 16, ; 36
	i32 4, ; 37
	i32 107, ; 38
	i32 100, ; 39
	i32 93, ; 40
	i32 25, ; 41
	i32 43, ; 42
	i32 104, ; 43
	i32 92, ; 44
	i32 85, ; 45
	i32 28, ; 46
	i32 77, ; 47
	i32 61, ; 48
	i32 84, ; 49
	i32 71, ; 50
	i32 40, ; 51
	i32 3, ; 52
	i32 51, ; 53
	i32 94, ; 54
	i32 63, ; 55
	i32 86, ; 56
	i32 78, ; 57
	i32 118, ; 58
	i32 16, ; 59
	i32 22, ; 60
	i32 68, ; 61
	i32 20, ; 62
	i32 18, ; 63
	i32 2, ; 64
	i32 59, ; 65
	i32 95, ; 66
	i32 32, ; 67
	i32 71, ; 68
	i32 55, ; 69
	i32 0, ; 70
	i32 77, ; 71
	i32 91, ; 72
	i32 6, ; 73
	i32 82, ; 74
	i32 93, ; 75
	i32 52, ; 76
	i32 44, ; 77
	i32 82, ; 78
	i32 92, ; 79
	i32 10, ; 80
	i32 5, ; 81
	i32 113, ; 82
	i32 25, ; 83
	i32 65, ; 84
	i32 75, ; 85
	i32 36, ; 86
	i32 57, ; 87
	i32 97, ; 88
	i32 113, ; 89
	i32 109, ; 90
	i32 76, ; 91
	i32 99, ; 92
	i32 110, ; 93
	i32 53, ; 94
	i32 23, ; 95
	i32 1, ; 96
	i32 90, ; 97
	i32 73, ; 98
	i32 41, ; 99
	i32 121, ; 100
	i32 17, ; 101
	i32 60, ; 102
	i32 9, ; 103
	i32 65, ; 104
	i32 78, ; 105
	i32 76, ; 106
	i32 69, ; 107
	i32 42, ; 108
	i32 29, ; 109
	i32 26, ; 110
	i32 94, ; 111
	i32 8, ; 112
	i32 83, ; 113
	i32 72, ; 114
	i32 37, ; 115
	i32 5, ; 116
	i32 63, ; 117
	i32 0, ; 118
	i32 105, ; 119
	i32 62, ; 120
	i32 4, ; 121
	i32 90, ; 122
	i32 109, ; 123
	i32 102, ; 124
	i32 88, ; 125
	i32 47, ; 126
	i32 12, ; 127
	i32 43, ; 128
	i32 42, ; 129
	i32 103, ; 130
	i32 79, ; 131
	i32 97, ; 132
	i32 14, ; 133
	i32 80, ; 134
	i32 38, ; 135
	i32 8, ; 136
	i32 70, ; 137
	i32 101, ; 138
	i32 18, ; 139
	i32 119, ; 140
	i32 99, ; 141
	i32 117, ; 142
	i32 37, ; 143
	i32 13, ; 144
	i32 116, ; 145
	i32 10, ; 146
	i32 88, ; 147
	i32 101, ; 148
	i32 120, ; 149
	i32 45, ; 150
	i32 11, ; 151
	i32 111, ; 152
	i32 20, ; 153
	i32 79, ; 154
	i32 105, ; 155
	i32 57, ; 156
	i32 15, ; 157
	i32 107, ; 158
	i32 108, ; 159
	i32 50, ; 160
	i32 52, ; 161
	i32 21, ; 162
	i32 46, ; 163
	i32 47, ; 164
	i32 74, ; 165
	i32 27, ; 166
	i32 49, ; 167
	i32 6, ; 168
	i32 55, ; 169
	i32 19, ; 170
	i32 74, ; 171
	i32 48, ; 172
	i32 36, ; 173
	i32 119, ; 174
	i32 75, ; 175
	i32 102, ; 176
	i32 87, ; 177
	i32 59, ; 178
	i32 72, ; 179
	i32 34, ; 180
	i32 66, ; 181
	i32 121, ; 182
	i32 85, ; 183
	i32 12, ; 184
	i32 91, ; 185
	i32 67, ; 186
	i32 115, ; 187
	i32 53, ; 188
	i32 7, ; 189
	i32 100, ; 190
	i32 58, ; 191
	i32 68, ; 192
	i32 24, ; 193
	i32 112, ; 194
	i32 56, ; 195
	i32 120, ; 196
	i32 70, ; 197
	i32 80, ; 198
	i32 3, ; 199
	i32 39, ; 200
	i32 11, ; 201
	i32 86, ; 202
	i32 122, ; 203
	i32 24, ; 204
	i32 23, ; 205
	i32 112, ; 206
	i32 31, ; 207
	i32 95, ; 208
	i32 62, ; 209
	i32 28, ; 210
	i32 67, ; 211
	i32 38, ; 212
	i32 122, ; 213
	i32 33, ; 214
	i32 66, ; 215
	i32 98, ; 216
	i32 89, ; 217
	i32 54, ; 218
	i32 83, ; 219
	i32 35, ; 220
	i32 110, ; 221
	i32 40, ; 222
	i32 106, ; 223
	i32 32, ; 224
	i32 81, ; 225
	i32 56, ; 226
	i32 117, ; 227
	i32 69, ; 228
	i32 51, ; 229
	i32 27, ; 230
	i32 9, ; 231
	i32 96, ; 232
	i32 46, ; 233
	i32 115, ; 234
	i32 48, ; 235
	i32 104, ; 236
	i32 22, ; 237
	i32 17, ; 238
	i32 39, ; 239
	i32 29, ; 240
	i32 64, ; 241
	i32 89, ; 242
	i32 45, ; 243
	i32 108, ; 244
	i32 64 ; 245
], align 4

@marshal_methods_number_of_classes = dso_local local_unnamed_addr constant i32 0, align 4

@marshal_methods_class_cache = dso_local local_unnamed_addr global [0 x %struct.MarshalMethodsManagedClass] zeroinitializer, align 4

; Names of classes in which marshal methods reside
@mm_class_names = dso_local local_unnamed_addr constant [0 x ptr] zeroinitializer, align 4

@mm_method_names = dso_local local_unnamed_addr constant [1 x %struct.MarshalMethodName] [
	%struct.MarshalMethodName {
		i64 0, ; id 0x0; name: 
		ptr @.MarshalMethodName.0_name; char* name
	} ; 0
], align 8

; get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr)
@get_function_pointer = internal dso_local unnamed_addr global ptr null, align 4

; Functions

; Function attributes: "min-legal-vector-width"="0" mustprogress nofree norecurse nosync "no-trapping-math"="true" nounwind "stack-protector-buffer-size"="8" uwtable willreturn
define void @xamarin_app_init(ptr nocapture noundef readnone %env, ptr noundef %fn) local_unnamed_addr #0
{
	%fnIsNull = icmp eq ptr %fn, null
	br i1 %fnIsNull, label %1, label %2

1: ; preds = %0
	%putsResult = call noundef i32 @puts(ptr @.str.0)
	call void @abort()
	unreachable 

2: ; preds = %1, %0
	store ptr %fn, ptr @get_function_pointer, align 4, !tbaa !3
	ret void
}

; Strings
@.str.0 = private unnamed_addr constant [40 x i8] c"get_function_pointer MUST be specified\0A\00", align 1

;MarshalMethodName
@.MarshalMethodName.0_name = private unnamed_addr constant [1 x i8] c"\00", align 1

; External functions

; Function attributes: noreturn "no-trapping-math"="true" nounwind "stack-protector-buffer-size"="8"
declare void @abort() local_unnamed_addr #2

; Function attributes: nofree nounwind
declare noundef i32 @puts(ptr noundef) local_unnamed_addr #1
attributes #0 = { "min-legal-vector-width"="0" mustprogress nofree norecurse nosync "no-trapping-math"="true" nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+armv7-a,+d32,+dsp,+fp64,+neon,+vfp2,+vfp2sp,+vfp3,+vfp3d16,+vfp3d16sp,+vfp3sp,-aes,-fp-armv8,-fp-armv8d16,-fp-armv8d16sp,-fp-armv8sp,-fp16,-fp16fml,-fullfp16,-sha2,-thumb-mode,-vfp4,-vfp4d16,-vfp4d16sp,-vfp4sp" uwtable willreturn }
attributes #1 = { nofree nounwind }
attributes #2 = { noreturn "no-trapping-math"="true" nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+armv7-a,+d32,+dsp,+fp64,+neon,+vfp2,+vfp2sp,+vfp3,+vfp3d16,+vfp3d16sp,+vfp3sp,-aes,-fp-armv8,-fp-armv8d16,-fp-armv8d16sp,-fp-armv8sp,-fp16,-fp16fml,-fullfp16,-sha2,-thumb-mode,-vfp4,-vfp4d16,-vfp4d16sp,-vfp4sp" }

; Metadata
!llvm.module.flags = !{!0, !1, !7}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!"Xamarin.Android remotes/origin/release/8.0.4xx @ 82d8938cf80f6d5fa6c28529ddfbdb753d805ab4"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
!7 = !{i32 1, !"min_enum_size", i32 4}
