#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using System.Reflection;
using SmartReader.Infrastructure.Utils.Epcis.Exceptions;

namespace SmartReader.Infrastructure.Utils.Epcis;

public abstract class Enumeration : IComparable
{
    protected Enumeration()
    {
    }

    protected Enumeration(short id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string DisplayName { get; }
    public short Id { get; }

    public int CompareTo(object other)
    {
        return Id.CompareTo(((Enumeration) other).Id);
    }

    public static IEnumerable<T> GetAll<T>() where T : Enumeration, new()
    {
        return typeof(T).GetTypeInfo().GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(x => x.GetValue(new T())).Cast<T>();
    }

    public static T GetByDisplayName<T>(string displayName) where T : Enumeration, new()
    {
        return GetAll<T>().SingleOrDefault(x => x.DisplayName == displayName) ?? throw NameException<T>(displayName);
    }

    public static T GetByDisplayNameInvariant<T>(string displayName) where T : Enumeration, new()
    {
        return GetAll<T>()
                   .SingleOrDefault(x =>
                       string.Equals(x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)) ??
               throw NameException<T>(displayName);
    }

    public static T GetById<T>(short id) where T : Enumeration, new()
    {
        return GetAll<T>().SingleOrDefault(x => x.Id == id) ?? throw IdException<T>(id);
    }

    public override int GetHashCode()
    {
        return 2108858624 + GetType().GetHashCode() + Id.GetHashCode();
    }

    public override string ToString()
    {
        return DisplayName;
    }

    public override bool Equals(object obj)
    {
        return obj is Enumeration other && GetType().Equals(obj.GetType()) && Id.Equals(other.Id);
    }

    private static Exception NameException<T>(string displayName)
    {
        return new EpcisException(ExceptionType.ValidationException,
            $"Invalid value for {typeof(T).Name} : '{displayName}'");
    }

    private static Exception IdException<T>(short id)
    {
        return new EpcisException(ExceptionType.ValidationException, $"Invalid ID for {typeof(T).Name} : {id}");
    }

    public static bool operator ==(Enumeration left, Enumeration right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(Enumeration left, Enumeration right)
    {
        return !(left == right);
    }
}