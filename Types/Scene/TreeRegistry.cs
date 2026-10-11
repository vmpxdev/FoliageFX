using System;
using System.Collections.Generic;
using UnityEngine;

// Finds the trees and decides which are this instance's. Every
// CustomUnityAsset atom in the scene is a tree, unless the scope is the
// containing atom alone (All Atoms off on a CustomUnityAsset). A FoliageFX on
// the tree's own atom wins: a scene-wide instance keeps such a tree in its
// list, Yielded, touches none of its materials, and takes it back on the
// first scan after that instance is removed. The containing atom (Self) is
// never yielded; this instance is the one on it.
//
// Releasing a tree must also give back what the effects own on its materials
// (copies, emitters, leaf materials), and the registry knows no effect:
// Lifecycle sets ReleaseEffects, which every Tree.Release calls once per
// material before the slots go back to their sources.
sealed class TreeRegistry
{
    public delegate void ReleaseFn(LeafMaterial m);

    public readonly List<Tree> Trees = new List<Tree>();
    public bool AllAtoms = true;
    public Atom Self;
    public ReleaseFn ReleaseEffects;
    // The looks, so a tree answers for its switch before it is gathered.
    public TreeLooks Looks;

    readonly OverlayLog log;

    public TreeRegistry(OverlayLog log)
    {
        this.log = log;
    }

    public int TreeCount { get { return Trees.Count; } }

    public int ConvertedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Trees.Count; i++) n += Trees[i].ConvertedCount;
            return n;
        }
    }

    // The scan tick (SceneScanSeconds). VaM hands out the atom list and an
    // atom's storable ids as fresh lists, so this is the one regular step that
    // allocates, a few small lists every two seconds; the frames between
    // allocate nothing.
    public void Scan(bool allAtoms)
    {
        AllAtoms = allAtoms;
        bool everyAtom = allAtoms || Self == null || Self.type != FoliageFXConfig.TreeAtomType;
        for (int i = Trees.Count - 1; i >= 0; i--)
        {
            Tree t = Trees[i];
            if (t.Atom == null || t.Loader == null || (!everyAtom && t.Atom != Self))
            {
                Drop(i);
                continue;
            }
            bool yield = t.Atom != Self && HasOwnInstance(t.Atom);
            if (yield != t.Yielded)
            {
                t.Release();
                t.Yielded = yield;
            }
            // A renderer the asset destroyed, a source gone, or materials
            // another instance had converted (it may have given them back):
            // read the asset again.
            else if (!t.Yielded && !t.NeedsGather && Stale(t))
            {
                t.NeedsGather = true;
                t.Dirty = true;
            }
        }
        if (everyAtom)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            List<Atom> atoms = sc.GetAtoms();
            for (int i = 0; i < atoms.Count; i++) Track(atoms[i]);
        }
        else Track(Self);
    }

    void Track(Atom atom)
    {
        if (atom == null || atom.type != FoliageFXConfig.TreeAtomType) return;
        for (int i = 0; i < Trees.Count; i++)
            if (Trees[i].Atom == atom) return;
        CustomUnityAssetLoader[] loaders = atom.GetComponentsInChildren<CustomUnityAssetLoader>(true);
        if (loaders.Length == 0) return;
        Tree t = new Tree(this, atom, loaders[0], atom == Self ? FoliageFXConfig.SelfTreeKey : atom.uid);
        t.Yielded = atom != Self && HasOwnInstance(atom);
        t.Hook(log);
        Trees.Add(t);
    }

    // An atom that left the scene takes its looks with it: a session plugin
    // would otherwise carry one scene's looks into the next, and a new atom
    // reusing a deleted uid would inherit them.
    void Drop(int index)
    {
        Tree t = Trees[index];
        t.Release();
        t.Unhook();
        Trees.RemoveAt(index);
        if (t.Atom == null && Looks != null && t.Key != FoliageFXConfig.SelfTreeKey) Looks.Forget(t.Key);
    }

    static bool Stale(Tree t)
    {
        for (int i = 0; i < t.Materials.Count; i++)
        {
            LeafMaterial m = t.Materials[i];
            if (m.Source == null) return true;
            // Another instance's copy is read again only once that instance
            // has given the slot back; while it holds it, the material is its.
            if (m.Foreign) { if (!m.SlotsHold(m.Source)) return true; continue; }
            // A slot something else wrote since (another instance's copy over
            // the source, or over this one's copy): read the tree again.
            if (!m.SlotsHold(m.Kind == LeafMaterial.KindOriginal ? m.Source : m.Copy)) return true;
            for (int r = 0; r < m.Renderers.Count; r++)
                if (m.Renderers[r] == null) return true;
        }
        return false;
    }

    public Tree Find(string uid)
    {
        for (int i = 0; i < Trees.Count; i++)
            if (Trees[i].Uid == uid) return Trees[i];
        return null;
    }

    public Tree FindByKey(string key)
    {
        for (int i = 0; i < Trees.Count; i++)
            if (Trees[i].Key == key) return Trees[i];
        return null;
    }

    public void MarkAllDirty()
    {
        for (int i = 0; i < Trees.Count; i++) Trees[i].Dirty = true;
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < Trees.Count; i++) Trees[i].Release();
    }

    // Everything released and every loader hook removed, the list emptied:
    // the plugin is going away, or starting over from nothing.
    public void Forget()
    {
        for (int i = Trees.Count - 1; i >= 0; i--) Drop(i);
    }

    // VaM's rename handler. The looks move in TreeLooks.Rename, which the
    // facade calls alongside; the containing atom's key stays SelfTreeKey.
    public void OnAtomRenamed(string oldUid, string newUid)
    {
        Tree t = Find(oldUid);
        if (t == null) return;
        t.Uid = newUid;
        if (t.Key != FoliageFXConfig.SelfTreeKey) t.Key = newUid;
    }

    // VaM names a plugin's storable plugin#N_<class>, so any id ending in the
    // suffix is a FoliageFX on that atom.
    public static bool HasOwnInstance(Atom atom)
    {
        if (atom == null) return false;
        List<string> ids = atom.GetStorableIDs();
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != null && ids[i].EndsWith(FoliageFXConfig.StorableSuffix, StringComparison.Ordinal)) return true;
        return false;
    }
}
