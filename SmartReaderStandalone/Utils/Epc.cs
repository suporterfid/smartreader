#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Newtonsoft.Json;
using System.Collections;

namespace SmartReader.Infrastructure.Utils;

[JsonConverter(typeof(EpcJsonConverter))]
public class Epc : IEquatable<Epc>, IComparable<Epc>, IComparable, IEnumerable<ushort>, IEnumerable
{
    private readonly ushort[] _epcArray;

    public Epc(IEnumerable<ushort> epcEnumerable)
    {
        _epcArray = (epcEnumerable?.ToArray()) ??
                    throw new ArgumentNullException(nameof(epcEnumerable));
    }

    public Epc(IEnumerable<byte> epcEnumerable)
    {
        var numArray = (epcEnumerable?.ToArray()) ??
                       throw new ArgumentNullException(nameof(epcEnumerable));
        _epcArray = new ushort[numArray.Length / 2];
        for (var index = 0; index < numArray.Length; index += 2)
        {
            var num1 = numArray[index] << 8;
            int num2 = numArray[index + 1];
            _epcArray[index / 2] = (ushort)(num1 | num2);
        }
    }

    public Epc(string epcString)
        : this(HexHelpers.HexToUshortEnumerable(epcString))
    {
    }

    public ushort this[int index] => _epcArray[index];

    public int Count => _epcArray.Length;

    public int CompareTo(object obj)
    {
        if (obj == null)
            return 1;
        return obj is Epc other ? CompareTo(other) : throw new ArgumentException("Object must be of type Epc");
    }

    public int CompareTo(Epc other)
    {
        if (other is null)
            return 1;
        var num1 = _epcArray.Length - other._epcArray.Length;
        if (num1 != 0)
            return num1;
        foreach (var data in _epcArray.Zip(other._epcArray, (b, b1) => new
        {
            T = b,
            O = b1
        }))
        {
            var num2 = data.T - data.O;
            if (num2 != 0)
                return num2;
        }

        return 0;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<ushort> GetEnumerator()
    {
        return ((IEnumerable<ushort>)_epcArray).GetEnumerator();
    }

    public bool Equals(Epc other)
    {
        return other is not null && _epcArray.SequenceEqual(other._epcArray);
    }

    public override bool Equals(object obj)
    {
        return obj != null && obj is Epc other && Equals(other);
    }

    public override int GetHashCode()
    {
        var num = _epcArray.Length;
        foreach (var epc in _epcArray)
            num = (num * 397) + epc.GetHashCode();
        return num;
    }

    public static bool operator ==(Epc a, Epc b)
    {
        return Equals(a, b);
    }

    public static bool operator !=(Epc a, Epc b)
    {
        return !(a == b);
    }

    public static bool operator <(Epc left, Epc right)
    {
        return Comparer<Epc>.Default.Compare(left, right) < 0;
    }

    public static bool operator >(Epc left, Epc right)
    {
        return Comparer<Epc>.Default.Compare(left, right) > 0;
    }

    public static bool operator <=(Epc left, Epc right)
    {
        return Comparer<Epc>.Default.Compare(left, right) <= 0;
    }

    public static bool operator >=(Epc left, Epc right)
    {
        return Comparer<Epc>.Default.Compare(left, right) >= 0;
    }

    public override string ToString()
    {
        var chArray = new char[_epcArray.Length * 4];
        for (var index = 0; index < _epcArray.Length; ++index)
        {
            var num1 = _epcArray[index] >> 12;
            chArray[index * 4] = (char)(55 + num1 + (((num1 - 10) >> 31) & -7));
            var num2 = (_epcArray[index] >> 8) & 15;
            chArray[(index * 4) + 1] = (char)(55 + num2 + (((num2 - 10) >> 31) & -7));
            var num3 = (_epcArray[index] >> 4) & 15;
            chArray[(index * 4) + 2] = (char)(55 + num3 + (((num3 - 10) >> 31) & -7));
            var num4 = _epcArray[index] & 15;
            chArray[(index * 4) + 3] = (char)(55 + num4 + (((num4 - 10) >> 31) & -7));
        }

        return new string(chArray);
    }
}