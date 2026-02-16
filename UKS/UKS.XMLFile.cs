/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */
using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Serialization;

namespace UKS;

public partial class UKS
{
    static string fileName = "";

    public string FileName { get => fileName; }

    //this is a modification of Thought which is used to store and retrieve the UKS in XML
    //it eliminates circular references by replacing Thought references with int indexed into an array 
    public class sThought
    {
        public int index;
        public string label = "";
        [DefaultValue(-1)]
        public int source = -1;
        [DefaultValue(-1)]
        public int linkType = -1;
        [DefaultValue(-1)]
        public int target = -1;
        [DefaultValue(1)]
        public float weight = 1;
        [DefaultValue(null)]
        public object V;
        public override string ToString()
        {
            return $"{index}, {label}";
        }
    }

    /// <summary>
    /// Saves the UKS content to an XML file.
    /// </summary>
    /// <param name="filenameIn">Optional file name; when null or empty, the previous file name is reused.</param>
    /// <returns><see langword="true"/> if the save succeeded; otherwise <see langword="false"/>.</returns>
    public bool SaveUKStoXMLFile(string filenameIn = "")
    {
        //if you don't pass in a file name, it uses the previous name
        if (!String.IsNullOrEmpty(filenameIn)) { fileName = filenameIn; }
        if (!CanWriteToFile(fileName, out string message))
        {
            Debug.WriteLine("Could not save file because: " + message);
            return false;
        }

        string tempFilePath = Path.GetTempFileName();
        UKSTemp.Clear();
        FormatContentForSaving("BrainSim");
        FormatContentForSaving("Thought");

        //List<Type> extraTypes = GetTypesInUKS();
        Stream file = File.Create(tempFilePath);
        file.Position = 0;
        try
        {
            XmlSerializer writer = new XmlSerializer(UKSTemp.GetType());
            writer.Serialize(file, UKSTemp);
            file.Close();
            File.Copy(tempFilePath, fileName, overwrite: true);
        }
        catch (Exception e)
        {
            if (e.InnerException is not null)
                Debug.WriteLine("Xml file write failed because: " + e.InnerException.Message);
            else
                Debug.WriteLine("Xml file write failed because: " + e.Message);
            return false;
        }
        finally
        {
            file.Close();
            UKSTemp = new();
        }
        return true;
    }

    //gets the index of a Thought in the output array
    //and creates an entry if it's not already there.
    private int GetIndex(Thought t)
    {
        if (t is null) return -1;
        if (string.IsNullOrWhiteSpace(t.Label))  // Put the GUID into the label only when it's unlabeled
            t.Label = $"unl_{Guid.NewGuid().ToString("N")[..8]}";
        int index = UKSTemp.FindIndex(x => x.label == t.Label);
        if (index == -1)
        {
            sThought st = new()
            {
                index = UKSTemp.Count,
                label = t.Label,
                V = t.V,
            };
            if (t is Link lnk)
            {
                st.source = GetIndex(lnk.From);
                st.linkType = GetIndex(lnk.LinkType);
                st.target = GetIndex(lnk.To);
            }
            index = UKSTemp.Count;
            UKSTemp.Add(st);
        }
        return index;
    }

    private void FormatContentForSaving(Thought root)
    {
        // TODO: Wipe transient data ...
        //foreach (Thought t in AllThoughts)
        GetIndex(root);
        foreach (var t in root.EnumerateSubThoughts())
        {
            string label = t.Label;
            if (string.IsNullOrWhiteSpace(label))  // Put the GUID into the label only when it's unlabeled
                label = $"unl_{Guid.NewGuid().ToString("N")[..8]}";
            int from = GetIndex((t as Link)?.From);
            int sType = GetIndex((t as Link)?.LinkType);
            int to = GetIndex((t as Link)?.To);
            if (UKSTemp.Count >= 170)
            { }

            sThought st = new()
            {
                index = UKSTemp.Count,
                label = label,
                source = from,
                linkType = sType,
                target = to,
                weight = t.Weight,
                V = t.V,
            };
            UKSTemp.Add(st);
        }
        RemoveTempLabels(root);
    }

    /// <summary>
    /// Checks whether the specified file can be opened for writing.
    /// </summary>
    /// <param name="fileName">Path to test for write access.</param>
    /// <param name="message">Outputs the error message if access fails.</param>
    /// <returns><see langword="true"/> if the file is writable; otherwise <see langword="false"/>.</returns>
    public static bool CanWriteToFile(string fileName, out string message)
    {
        FileStream file1;
        message = "";
        if (File.Exists(fileName))
        {
            try
            {
                file1 = File.Open(fileName, FileMode.Open);
                file1.Close();
                return true;
            }
            catch (Exception e)
            {
                message = e.Message;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Loads UKS content from a previously-saved XML file.
    /// </summary>
    /// <param name="filenameIn">Optional file name; when null or empty, the previous file name is reused.</param>
    /// <returns><see langword="true"/> if the load succeeded; otherwise <see langword="false"/>.</returns>
    public bool LoadUKSfromXMLFile(string filenameIn = "")
    {
        //stash the current BrainSim configuration
        var contentToRestore = ExtractPortionOfUKS(Labeled("BrainSim"));

        Stream file;
        if (!String.IsNullOrEmpty(filenameIn)) { fileName = filenameIn; }
        try
        {
            file = File.Open(fileName, FileMode.Open, FileAccess.Read);
        }
        catch (Exception e)
        {
            Debug.WriteLine("Could not open file because: " + e.Message);
            return false;
        }

        List<Type> extraTypes = new();
        XmlSerializer reader1 = new XmlSerializer(UKSTemp.GetType(), extraTypes.ToArray());
        try
        {
            UKSTemp = (List<sThought>)reader1.Deserialize(file);
        }
        catch (Exception e)
        {
            file.Close();
            Debug.WriteLine("Network file load failed, a blank network will be opened. \r\n\r\n" + e.InnerException);//, "File Load Error",
            return false;
        }
        file.Close();

        DeFormatContentAfterLoading();

        //EVERYTHING below is for compatibility with older xml files.

        AddBrainSimConfigSectionIfNeeded();

        if (Labeled("BrainSim") is null)
        {
            MergeStringListIntoUKS(contentToRestore);
        }

        //more hacks for compatibility old file formatting
        //this does nothought on updated file content
        AddStatement("inheritable", "is-a", "Property");
        Thought hasChild = Labeled("has-child");
        if (hasChild is not null)
        {
            hasChild.AddLink("inverseOf", "is-a");
            hasChild.RemoveLink1("hasProperty", "isTransitive");
            hasChild.RemoveLink1("hasProperty", "inheritable");
        }
        Thought isA = Labeled("is-a");
        if (isA is not null)
        {
            isA.AddLink("hasProperty", "inheritable");
            isA.AddLink("hasProperty", "isTransitive");
            isA.RemoveLink1("inverseOf","has-child");
            isA.RemoveLink1("hasProperty",null);
        }
        Thought has = Labeled("has");
        if (has is not null)
        {
            has.AddLink("hasProperty","inheritable");
        }
        return true;
    }

    private List<string> ExtractPortionOfUKS(Thought root)
    {
        List<string> uksContent = new List<string>();
        if (root is null) return uksContent;
        foreach (var descendant in root.Descendants)
        {
            foreach (var r in descendant.LinksTo)
            {
                uksContent.Add(r.ToString());
            }
        }
        return uksContent;
    }

    private void MergeStringListIntoUKS(List<string> contentToRestore)
    {
        AddThought("BrainSim", null);
        foreach (string s in contentToRestore)
        {
            string[] strings = s.Split("->");
        }
    }

    private void DeFormatContentAfterLoading()
    {
        AllThoughts.Clear();
        ThoughtLabels.ClearLabelList();
        //get all the thoughts
        foreach (sThought st in UKSTemp)
        {
            if (st.source == -1 && st.linkType == -1 && st.target == -1)
            {
                Thought t = new()
                {
                    Label = st.label,
                    Weight = st.weight,
                    V = st.V,
                };
                AllThoughts.Add(t);
            }
            else
            {
                Link t = new()
                {
                    Label = st.label,
                    Weight = st.weight,
                    V = st.V,
                };
                if (st.source != -1)
                    t.From = theUKS.Labeled(UKSTemp[st.source].label);
                if (st.linkType != -1)
                    t.LinkType = theUKS.Labeled(UKSTemp[st.linkType].label);
                if (st.target != -1)
                    t.To = theUKS.Labeled(UKSTemp[st.target].label);
                AllThoughts.Add(t);
                t.From?.LinksToWriteable.Add(t);
            }
        }

        //re-create reverse links
        foreach (Thought t in AllThoughts)
        {
            foreach (Link r in t.LinksTo)
            {
                Thought t1 = r.To;
                if (t1 is not null)
                    if (!t1.LinksFromWriteable.Contains(r))
                        t1.LinksFromWriteable.Add(r);
            }
        }

        RemoveTempLabels("Thought");
        RemoveTempLabels("BrainSim");
    }
}
