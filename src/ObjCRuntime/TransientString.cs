using System;
using System.Runtime.InteropServices;

#nullable enable

namespace ObjCRuntime {
	// a short-lived holder for a C-like string for native interop
	// typical usage:
	// using var cstring = new NativeString (str);
	// SomePInvoke (cstring);
	//
	// If SomePInvoke doesn't make a copy, this is not the right tool
	// for you.
	internal struct TransientString : IDisposable {
		IntPtr ptr;
		public enum Encoding {
			Auto = 0,
			BStr,
			Ansi, // aka LPStr
			Unicode,
		};


		public TransientString (string? str, Encoding encoding = Encoding.Auto)
		{
			switch (encoding) {
			case Encoding.Auto:
				ptr = Marshal.StringToHGlobalAuto (str);
				break;
			case Encoding.BStr:
				ptr = Marshal.StringToBSTR (str);
				break;
			case Encoding.Ansi:
				ptr = Marshal.StringToHGlobalAnsi (str);
				break;
			case Encoding.Unicode:
				ptr = Marshal.StringToHGlobalUni (str);
				break;
			default:
				throw new ArgumentOutOfRangeException (nameof (encoding));
			}
		}

		public void Dispose ()
		{
			if (ptr != IntPtr.Zero) {
				Marshal.FreeHGlobal (ptr);
				ptr = IntPtr.Zero;
			}
		}

		public static implicit operator IntPtr (TransientString str) => str.ptr;


		public static string? ToStringAndFree (IntPtr ptr, Encoding encoding = Encoding.Auto)
		{
			string? result = null;
			switch (encoding) {
			case Encoding.Auto:
				result = Marshal.PtrToStringAuto (ptr);
				break;
			case Encoding.BStr:
				result = Marshal.PtrToStringBSTR (ptr);
				break;
			case Encoding.Ansi:
				result = Marshal.PtrToStringAnsi (ptr);
				break;
			case Encoding.Unicode:
				result = Marshal.PtrToStringUni (ptr);
				break;
			default:
				throw new ArgumentOutOfRangeException (nameof (encoding));
			}
			Marshal.FreeHGlobal (ptr);
			return result;
		}
	}
}