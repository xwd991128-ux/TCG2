using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 反射字段序列化工具：将效果/条件/过滤器的 public 字段转为字符串（引用用 id），并支持还原
    /// 支持类型：string、int、long、float、double、bool、枚举、数组、以及有 id 的 ScriptableObject
    /// StatusData 特殊处理（无 id，用 effect 枚举名引用）
    /// 跳过：Sprite/GameObject/AudioClip 等 Unity 资源引用、static 字段
    /// </summary>
    public static class ReflectionUtil
    {
        private const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Instance;

        // 序列化对象的所有 public 实例字段到 fields
        public static void SerializeFields(object obj, List<FieldCustomData> fields)
        {
            if (obj == null || fields == null)
                return;

            Type type = obj.GetType();
            foreach (FieldInfo field in type.GetFields(FLAGS))
            {
                if (field.IsStatic)
                    continue;

                object val = field.GetValue(obj);
                string str = FieldToString(field.FieldType, val);
                if (str != null)
                    fields.Add(new FieldCustomData { name = field.Name, value = str });
            }
        }

        // 按 fields 还原对象字段
        public static void DeserializeFields(object obj, List<FieldCustomData> fields)
        {
            if (obj == null || fields == null)
                return;

            Type type = obj.GetType();
            foreach (FieldCustomData fd in fields)
            {
                FieldInfo field = type.GetField(fd.name, FLAGS);
                if (field == null)
                    continue;

                object val = StringToField(field.FieldType, fd.value);
                if (val != null)
                    field.SetValue(obj, val);
            }
        }

        // 字段值 → 字符串
        private static string FieldToString(Type fieldType, object val)
        {
            if (val == null)
                return null;

            if (fieldType == typeof(string))
                return (string)val;

            if (fieldType == typeof(int))
                return ((int)val).ToString();
            if (fieldType == typeof(long))
                return ((long)val).ToString();
            if (fieldType == typeof(float))
                return ((float)val).ToString(CultureInfo.InvariantCulture);
            if (fieldType == typeof(double))
                return ((double)val).ToString(CultureInfo.InvariantCulture);
            if (fieldType == typeof(bool))
                return ((bool)val) ? "1" : "0";

            if (fieldType.IsEnum)
                return val.ToString();

            if (fieldType.IsSubclassOf(typeof(ScriptableObject)))
            {
                // StatusData 无 id，用 effect 枚举名
                if (val is StatusData sdata)
                    return sdata.effect.ToString();

                // 其余有 id 字段的类型（CardData/AbilityData/TraitData/TeamData/RarityData/PackData）
                FieldInfo idField = fieldType.GetField("id", FLAGS);
                if (idField != null && idField.FieldType == typeof(string))
                    return (string)idField.GetValue(val);

                return null; // Sprite/GameObject/AudioClip 等资源引用跳过
            }

            if (fieldType.IsArray)
            {
                Type elemType = fieldType.GetElementType();
                Array arr = (Array)val;
                List<string> parts = new List<string>();
                foreach (object elem in arr)
                {
                    string s = FieldToString(elemType, elem);
                    if (s != null)
                        parts.Add(s);
                }
                return string.Join(",", parts);
            }

            return null;
        }

        // 字符串 → 字段值
        private static object StringToField(Type fieldType, string str)
        {
            if (string.IsNullOrEmpty(str))
                return null;

            if (fieldType == typeof(string))
                return str;

            if (fieldType == typeof(int))
                return int.Parse(str);
            if (fieldType == typeof(long))
                return long.Parse(str);
            if (fieldType == typeof(float))
                return float.Parse(str, CultureInfo.InvariantCulture);
            if (fieldType == typeof(double))
                return double.Parse(str, CultureInfo.InvariantCulture);
            if (fieldType == typeof(bool))
                return str == "1";

            if (fieldType.IsEnum)
                return Enum.Parse(fieldType, str);

            if (fieldType.IsSubclassOf(typeof(ScriptableObject)))
            {
                if (fieldType == typeof(StatusData))
                    return StatusData.Get((StatusType)Enum.Parse(typeof(StatusType), str));
                if (fieldType == typeof(TeamData))
                    return TeamData.Get(str);
                if (fieldType == typeof(RarityData))
                    return RarityData.Get(str);
                if (fieldType == typeof(TraitData))
                    return TraitData.Get(str);
                if (fieldType == typeof(CardData))
                    return CardData.Get(str);
                if (fieldType == typeof(AbilityData))
                    return AbilityData.Get(str);
                if (fieldType == typeof(PackData))
                    return PackData.Get(str);
                return null;
            }

            if (fieldType.IsArray)
            {
                Type elemType = fieldType.GetElementType();
                string[] strs = str.Split(',');
                Array arr = Array.CreateInstance(elemType, strs.Length);
                for (int i = 0; i < strs.Length; i++)
                {
                    object v = StringToField(elemType, strs[i]);
                    if (v != null)
                        arr.SetValue(v, i);
                }
                return arr;
            }

            return null;
        }
    }
}
