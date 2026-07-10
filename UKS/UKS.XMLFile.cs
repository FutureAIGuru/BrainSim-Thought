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
using System.Xml;
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
        public object? V;
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
        if (Labeled("BrainSim") is Thought brainSim)
            FormatContentForSaving(brainSim);
        if (Labeled("Thought") is Thought thoughtRoot)
            FormatContentForSaving(thoughtRoot);

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
    private int GetIndex(Thought? t)
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
                weight = t.Weight,
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

    private void FormatContentForSaving(Thought? root)
    {
        if (root is null) return;
        GetIndex(root);
        foreach (var t in root.EnumerateSubThoughts())
            GetIndex(t);
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
            UKSTemp = (List<sThought>?)reader1.Deserialize(file) ?? new();
        }
        catch (Exception e)
        {
            file.Close();
            Debug.WriteLine("Network file load failed, a blank network will be opened. \r\n\r\n" + e.InnerException);//, "File Load Error",
            return false;
        }
        file.Close();

        DeFormatContentAfterLoading();
        ApplyPostXmlLoadCompatibility(contentToRestore);
        return true;
    }

    /// <summary>
    /// Deserializes UKS XML into the temporary thought list without mutating a UKS instance.
    /// Used by test fixtures to cache Algorithm.xml across many test cases.
    /// </summary>
    internal static List<sThought> DeserializeUkTempFromXmlFile(string path)
    {
        using Stream file = File.Open(path, FileMode.Open, FileAccess.Read);
        XmlSerializer reader = new(typeof(List<sThought>));
        return (List<sThought>?)reader.Deserialize(file) ?? new();
    }

    /// <summary>
    /// Rehydrates a UKS from a cached snapshot (caller must pass a deep copy per test).
    /// </summary>
    internal void RestoreFromUkTempSnapshot(List<sThought> snapshot)
    {
        UKSTemp = snapshot;
        DeFormatContentAfterLoading();
        ApplyPostXmlLoadCompatibility();
    }

    private void ApplyPostXmlLoadCompatibility(List<string>? brainSimContentToRestore = null)
    {
        //EVERYTHING below is for compatibility with older xml files.

        AddBrainSimConfigSectionIfNeeded();

        if (Labeled("BrainSim") is null && brainSimContentToRestore is not null)
        {
            MergeStringListIntoUKS(brainSimContentToRestore);
        }

        //more hacks for compatibility old file formatting
        //this does nothought on updated file content
        AddStatement("inheritable", "is-a", "Property");
        if (Labeled("has-child") is Thought hasChild)
        {
            Thought? inverseOf = "inverseOf";
            Thought? isAChild = "is-a";
            Thought? hasProperty = "hasProperty";
            Thought? isTransitive = "isTransitive";
            Thought? inheritable = "inheritable";
            if (inverseOf is not null && isAChild is not null)
                hasChild.AddLink(inverseOf, isAChild);
            if (hasProperty is not null && isTransitive is not null)
                hasChild.RemoveLink(hasProperty, isTransitive);
            if (hasProperty is not null && inheritable is not null)
                hasChild.RemoveLink(hasProperty, inheritable);
        }
        if (Labeled("is-a") is Thought isA)
        {
            Thought? hasProperty = "hasProperty";
            Thought? inheritable = "inheritable";
            Thought? isTransitive = "isTransitive";
            Thought? inverseOf = "inverseOf";
            Thought? hasChildLink = "has-child";
            if (hasProperty is not null && inheritable is not null)
                isA.AddLink(hasProperty, inheritable);
            if (hasProperty is not null && isTransitive is not null)
                isA.AddLink(hasProperty, isTransitive);
            if (inverseOf is not null && hasChildLink is not null)
                isA.RemoveLink(inverseOf, hasChildLink);
            if (hasProperty is not null)
                isA.RemoveLink(hasProperty, null!);
        }
        if (Labeled("has") is Thought has)
        {
            Thought? hasProperty = "hasProperty";
            Thought? inheritable = "inheritable";
            if (hasProperty is not null && inheritable is not null)
                has.AddLink(hasProperty, inheritable);
        }
    }

    private List<string> ExtractPortionOfUKS(Thought? root)
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
            if (string.IsNullOrWhiteSpace(s)) continue;
            ProcessSingleLine(s.Trim());
        }
    }

    private void DeFormatContentAfterLoading()
    {
        AtomicThoughts.Clear();
        ThoughtLabels.ClearLabelList();
        //3 passes:  1) find all the labels and allocate thoughts 2) fill in links by label  3) remove temp labels from unlabeled thoughts
        //allocate all the thoughts
        foreach (sThought st in UKSTemp)
        {
            if (st.source == -1 || st.linkType == -1)
            {
                Thought t = new()
                {
                    Label = st.label,
                    Weight = st.weight,
                    V = st.V,
                };
                t.TimeToLive = TimeSpan.MaxValue;
                AtomicThoughts.Add(t);
            }
            else //this must be a link
            {
                Link l = new()
                {
                    Label = st.label,
                    Weight = st.weight,
                    V = st.V,
                };
                l.TimeToLive = TimeSpan.MaxValue;
                //AtomicThoughts.Add(l);
            }
        }
        //add contents of links
        foreach (sThought st in UKSTemp)
        {
            if (st.source == -1 || st.linkType == -1)
            {
            }
            else //this must be a link
            {
                Thought? from = Labeled(UKSTemp[st.source].label);
                Thought? linkType = Labeled(UKSTemp[st.linkType].label);
                Thought? to = st.target != -1 ? Labeled(UKSTemp[st.target].label) : null;
                if (Labeled(st.label) is not Link theLink)
                    continue;
                theLink.To = to;
                theLink.From = from;
                theLink.LinkType = linkType;
                theLink.Weight = st.weight;
                theLink.V = st.V;
                theLink.TimeToLive = TimeSpan.MaxValue;
                if (theLink.From is null || theLink.LinkType is null)
                    continue;
                Link? newLink = theLink.From.AddLink(theLink.LinkType, theLink.To);
                if (newLink is null)
                    continue;
                newLink.Weight = st.weight;
                if (!AtomicThoughts.Contains(newLink))
                    AtomicThoughts.Add(newLink);
                if (linkType?.Label == "VLU")
                    PromoteToSeqElement(theLink.From);
            }
        }
 
        //remove temporary labels
        foreach (sThought st in UKSTemp)
        {
            if (st.label.StartsWith("unl_"))
            {
                Thought? x = Labeled(st.label);
                if (x is not null)
                {
                    x.Label = "";
                }
            }
        }
    }
}
