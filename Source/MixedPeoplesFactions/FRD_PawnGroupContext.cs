using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public static class FRD_PawnGroupContext
    {
        private struct Frame
        {
            public Faction Faction;
            public PawnGroupKindDef Kind;
            public bool Ordinary;
        }

        [ThreadStatic]
        private static Stack<Frame> frames;

        [ThreadStatic]
        private static int pawnGenerationDepth;

        public static void Push(PawnGroupMakerParms parms)
        {
            if (frames == null)
            {
                frames = new Stack<Frame>();
            }
            PawnGroupKindDef kind = parms?.groupKind;
            frames.Push(new Frame
            {
                Faction = parms?.faction,
                Kind = kind,
                Ordinary = parms?.faction?.def?.humanlikeFaction == true
            });
        }

        public static void Pop()
        {
            if (frames != null && frames.Count > 0)
            {
                frames.Pop();
            }
        }


        public static void EnterPawnGeneration()
        {
            pawnGenerationDepth++;
        }

        public static void ExitPawnGeneration()
        {
            if (pawnGenerationDepth > 0)
            {
                pawnGenerationDepth--;
            }
        }

        public static Faction EffectiveFactionFor(Faction requestFaction)
        {
            return pawnGenerationDepth == 1 ? CurrentFaction ?? requestFaction : requestFaction;
        }

        public static Faction CurrentFaction
        {
            get
            {
                return frames != null && frames.Count > 0 ? frames.Peek().Faction : null;
            }
        }


        public static bool IsCombatGroupFor(Faction faction)
        {
            if (frames == null || frames.Count == 0)
            {
                return false;
            }
            Frame frame = frames.Peek();
            return pawnGenerationDepth == 1
                && frame.Ordinary
                && frame.Faction != null
                && ReferenceEquals(frame.Faction, faction)
                && ReferenceEquals(frame.Kind, PawnGroupKindDefOf.Combat);
        }

        public static bool IsOrdinaryGroupFor(Faction faction)
        {
            if (frames == null || frames.Count == 0)
            {
                return false;
            }
            Frame frame = frames.Peek();
            return pawnGenerationDepth == 1 && frame.Ordinary && frame.Faction != null && ReferenceEquals(frame.Faction, faction);
        }
    }
}
