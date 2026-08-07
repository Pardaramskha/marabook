using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UniversSale.Tests
{
    /// <summary>Comparaison structurelle champ à champ, par réflexion, des
    /// objets du modèle (namespace UniversSale.Model). C'est elle qui attrape
    /// un champ ajouté au modèle mais oublié par la sérialisation ou par
    /// PivotEdit.Clone : le champ apparaît, le test échoue.</summary>
    public static class DeepCompare
    {
        /// <summary>Différences entre deux graphes (vide = identiques).
        /// skip : « Type.Champ » à ignorer (transitoires légitimes).</summary>
        public static List<string> Diff(object expected, object actual, HashSet<string> skip)
        {
            var diffs = new List<string>();
            Walk(expected, actual, "", skip ?? new HashSet<string>(), diffs, 0);
            return diffs;
        }

        private static void Walk(object a, object b, string path, HashSet<string> skip,
            List<string> diffs, int depth)
        {
            if (diffs.Count > 40) return; // le rapport est déjà illisible au-delà
            if (depth > 64) { diffs.Add(path + " : graphe trop profond"); return; }
            if (a == null && b == null) return;
            if (a == null || b == null)
            {
                diffs.Add(path + " : « " + Text(a) + " » ≠ « " + Text(b) + " »");
                return;
            }
            var type = a.GetType();
            if (type != b.GetType())
            {
                diffs.Add(path + " : types " + type.Name + " ≠ " + b.GetType().Name);
                return;
            }
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum
                || type == typeof(double) || type == typeof(decimal))
            {
                if (!a.Equals(b))
                    diffs.Add(path + " : « " + Text(a) + " » ≠ « " + Text(b) + " »");
                return;
            }
            var bytesA = a as byte[];
            if (bytesA != null)
            {
                var bytesB = (byte[])b;
                if (bytesA.Length != bytesB.Length)
                {
                    diffs.Add(path + " : " + bytesA.Length + " octets ≠ " + bytesB.Length);
                    return;
                }
                for (var i = 0; i < bytesA.Length; i++)
                    if (bytesA[i] != bytesB[i])
                    {
                        diffs.Add(path + " : octet " + i + " différent");
                        return;
                    }
                return;
            }
            var dictA = a as IDictionary;
            if (dictA != null)
            {
                var dictB = (IDictionary)b;
                if (dictA.Count != dictB.Count)
                {
                    diffs.Add(path + " : " + dictA.Count + " entrées ≠ " + dictB.Count);
                    return;
                }
                foreach (var key in dictA.Keys)
                {
                    if (!dictB.Contains(key))
                    {
                        diffs.Add(path + "[" + key + "] : absent à droite");
                        continue;
                    }
                    Walk(dictA[key], dictB[key], path + "[" + key + "]", skip, diffs, depth + 1);
                }
                return;
            }
            var listA = a as IList;
            if (listA != null)
            {
                var listB = (IList)b;
                if (listA.Count != listB.Count)
                {
                    diffs.Add(path + " : " + listA.Count + " éléments ≠ " + listB.Count);
                    return;
                }
                for (var i = 0; i < listA.Count; i++)
                    Walk(listA[i], listB[i], path + "[" + i + "]", skip, diffs, depth + 1);
                return;
            }
            if (type.Namespace != null && type.Namespace.StartsWith("UniversSale", StringComparison.Ordinal))
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (skip.Contains(type.Name + "." + field.Name)) continue;
                    Walk(field.GetValue(a), field.GetValue(b),
                        path + "." + field.Name, skip, diffs, depth + 1);
                }
                return;
            }
            if (!a.Equals(b))
                diffs.Add(path + " : « " + Text(a) + " » ≠ « " + Text(b) + " » (" + type.Name + ")");
        }

        private static string Text(object value)
        {
            return value == null ? "null" : value.ToString();
        }
    }
}
