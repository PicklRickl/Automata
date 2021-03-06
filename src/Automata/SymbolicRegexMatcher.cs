﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Automata.Utilities;
using System.Numerics;
using System.Linq;

namespace Microsoft.Automata
{
    /// <summary>
    /// Helper class of symbolic regex for finding matches
    /// </summary>
    /// <typeparam name="S"></typeparam>
    internal class SymbolicRegexMatcher<S>
    {
        SymbolicRegexBuilder<S> builder;

        /// <summary>
        /// Original regex.
        /// </summary>
        SymbolicRegex<S> A;

        /// <summary>
        /// Set of elements that matter as first element of A. 
        /// </summary>
        BooleanDecisionTree A_StartSet;

        /// <summary>
        /// Number of elements in A_StartSet
        /// </summary>
        int A_StartSet_Size;

        //Vector<ushort> A_First_Vec;

        //Vector<ushort> A_Second_Vec;

        /// <summary>
        /// If not null then contains all relevant start characters as vectors
        /// </summary>
        Vector<ushort>[] A_StartSet_Vec = null;

        ///// <summary>
        ///// Set of first byte of UTF8 encoded characters.
        ///// Characters that matter are mapped to true. 
        ///// Characters that dont matter are mapped to false.
        ///// This array has size 256.
        ///// </summary>
        //bool[] A_StartSetAsByteArray = new bool[256];

        /// <summary>
        /// if nonempty then A has that fixed prefix
        /// </summary>
        string A_prefix;

        /// <summary>
        /// if nonempty then A has that fixed prefix
        /// </summary>>
        byte[] A_prefixUTF8;

        /// <summary>
        /// First byte of A_prefixUTF8 in vector
        /// </summary>
        Vector<byte> A_prefixUTF8_first_byte;

        /// <summary>
        /// predicate array corresponding to fixed prefix of A
        /// </summary>
        S[] A_prefix_array;

        /// <summary>
        /// if true then the fixed prefix of A is idependent of case
        /// </summary>
        bool A_fixedPrefix_ignoreCase;

        /// <summary>
        /// precomputed state of A1 that is reached after the fixed prefix of A
        /// </summary>
        int A1_skipState;

        /// <summary>
        /// precomputed regex of A1 that is reached after the fixed prefix of A
        /// </summary>
        SymbolicRegex<S> A1_skipStateRegex;

        /// <summary>
        /// Reverse(A).
        /// </summary>
        SymbolicRegex<S> Ar;

        /// <summary>
        /// if nonempty then Ar has that fixed prefix of predicates
        /// </summary>
        S[] Ar_prefix;

        /// <summary>
        /// precomputed state that is reached after the fixed prefix of Ar
        /// </summary>
        int Ar_skipState;

        /// <summary>
        /// precomputed regex that is reached after the fixed prefix of Ar
        /// </summary>
        SymbolicRegex<S> Ar_skipStateRegex;

        /// <summary>
        /// .*A
        /// </summary>
        SymbolicRegex<S> A1;

        /// <summary>
        /// Variant of A1 for matching.
        /// In A2 anchors have been removed. 
        /// Used only by IsMatch and when A contains anchors.
        /// </summary>
        SymbolicRegex<S> A2 = null;

        /// <summary>
        /// Used only by IsMatch and if A2 is used.
        /// </summary>
        int q0_A2 = 0;

        /// <summary>
        /// Initial state of A1 (0 is not used).
        /// </summary>
        int q0_A1 = 1;

        /// <summary>
        /// Initial state of Ar (0 is not used).
        /// </summary>
        int q0_Ar = 2;

        /// <summary>
        /// Initial state of A (0 is not used).
        /// </summary>
        int q0_A = 3;

        /// <summary>
        /// Next available state id.
        /// </summary>
        int nextStateId = 4;

        /// <summary>
        /// Initialized to atoms.Length.
        /// </summary>
        readonly int K;

        /// <summary>
        /// Partition of the input space of predicates.
        /// Length of atoms is K.
        /// </summary>
        S[] atoms;

        /// <summary>
        /// Maps each character into a partition id in the range 0..K-1.
        /// </summary>
        DecisionTree dt;

        /// <summary>
        /// Maps regexes to state ids
        /// </summary>
        Dictionary<SymbolicRegex<S>, int> regex2state = new Dictionary<SymbolicRegex<S>, int>();

        /// <summary>
        /// Maps states >= StateLimit to regexes.
        /// </summary>
        Dictionary<int, SymbolicRegex<S>> state2regexExtra = new Dictionary<int, SymbolicRegex<S>>();

        /// <summary>
        /// Maps states 1..(StateLimit-1) to regexes. 
        /// State 0 is not used but is reserved for denoting UNDEFINED value.
        /// Length of state2regex is StateLimit. Entry 0 is not used.
        /// </summary>
        SymbolicRegex<S>[] state2regex;

        /// <summary>
        /// Overflow from delta. Transitions with source state over the limit.
        /// Each entry (q, [p_0...p_n]) has n = atoms.Length-1 and represents the transitions q --atoms[i]--> p_i.
        /// All defined states are strictly positive, p_i==0 means that q --atoms[i]--> p_i is still undefined.
        /// </summary>
        Dictionary<int, int[]> deltaExtra = new Dictionary<int, int[]>();

        /// <summary>
        /// Bound on the maximum nr of states stored in array.
        /// </summary>
        internal readonly int StateLimit;

        /// <summary>
        /// Bound on the maximum nr of chars that trigger vectorized IndexOf.
        /// </summary>
        internal readonly int StartSetSizeLimit;

        /// <summary>
        /// Holds all transitions for states 1..MaxNrOfStates-1.
        /// each transition q ---atoms[i]---> p is represented by entry p = delta[(q * K) + i]. 
        /// Length of delta is K*StateLimit.
        /// </summary>
        int[] delta;

        /// <summary>
        /// Constructs matcher for given symbolic regex
        /// </summary>
        /// <param name="sr">given symbolic regex</param>
        /// <param name="StateLimit">limit on the number of states kept in a preallocated array (default is 1000)</param>
        internal SymbolicRegexMatcher(SymbolicRegex<S> sr, int StateLimit = 10000, int startSetSizeLimit = 1)
        {
            this.StartSetSizeLimit = startSetSizeLimit;
            this.builder = sr.builder;
            this.StateLimit = StateLimit;
            if (sr.Solver is BVAlgebra)
            {
                BVAlgebra bva = sr.Solver as BVAlgebra;
                atoms = bva.atoms as S[];
                dt = bva.dtree;
            }
            else if (sr.Solver is CharSetSolver)
            {
                atoms = sr.ComputeMinterms();
                dt = DecisionTree.Create(sr.Solver as CharSetSolver, atoms as BDD[]);
            }
            else
            {
                throw new NotSupportedException(string.Format("only {0} or {1} solver is supported", typeof(BVAlgebra), typeof(CharSetSolver)));
            }

            this.A = sr;
            this.Ar = sr.Reverse();
            this.A1 = sr.builder.MkConcat(sr.builder.dotStar, sr);
            this.regex2state[A1] = q0_A1;
            this.regex2state[Ar] = q0_Ar;
            this.regex2state[A] = q0_A;
            this.K = atoms.Length;
            this.delta = new int[K * StateLimit];
            this.state2regex = new SymbolicRegex<S>[StateLimit];
            if (q0_A1 < StateLimit)
            {
                this.state2regex[q0_A1] = A1;
            }
            else
            {
                this.state2regexExtra[q0_A1] = A1;
                this.deltaExtra[q0_A1] = new int[K];
            }

            if (q0_Ar < StateLimit)
            {
                this.state2regex[q0_Ar] = Ar;
            }
            else
            {
                this.state2regexExtra[q0_Ar] = Ar;
                this.deltaExtra[q0_Ar] = new int[K];
            }

            if (q0_A < StateLimit)
            {
                this.state2regex[q0_A] = A;
            }
            else
            {
                this.state2regexExtra[q0_A] = A;
                this.deltaExtra[q0_A] = new int[K];
            }

            BDD A_startSet_BDD = builder.solver.ConvertToCharSet(A.GetStartSet());
            this.A_StartSet_Size = (int)builder.solver.CharSetProvider.ComputeDomainSize(A_startSet_BDD);
            if (A_StartSet_Size <= startSetSizeLimit)
            {
                char[] startchars = new List<char>(builder.solver.CharSetProvider.GenerateAllCharacters(A_startSet_BDD)).ToArray();
                A_StartSet_Vec = Array.ConvertAll(startchars, c => new Vector<ushort>(c));
            }
            this.A_StartSet = BooleanDecisionTree.Create(builder.solver.CharSetProvider, A_startSet_BDD);

            ////consider the UTF8 encoded first byte
            //for (ushort i = 0; i < 128; i++)
            //{
            //    //relevant ASCII characters
            //    this.A_StartSetAsByteArray[i] = this.A_StartSet.Contains(i);
            //}
            ////to be on the safe side, set all other bytes to be relevant
            ////TBD: set only those bytes to be relevant 
            ////that are potentially the first byte encoding of a relevant character
            //for (ushort i = 128; i < 256; i++)
            //{
            //    //ASCII is not encoded
            //    this.A_StartSetAsByteArray[i] = true;
            //}

            SymbolicRegex<S> tmp = A;
            this.A_prefix_array = A.GetPrefix();
            this.A_prefix = A.FixedPrefix;
            this.A_prefixUTF8 = System.Text.UnicodeEncoding.UTF8.GetBytes(this.A_prefix);
            if (this.A_prefix != string.Empty)
            {
                this.A_prefixUTF8_first_byte = new Vector<byte>(this.A_prefixUTF8[0]);
            }
            this.A_fixedPrefix_ignoreCase = A.IgnoreCaseOfFixedPrefix;
            this.A1_skipState = DeltaPlus(A_prefix, q0_A1, out tmp);
            this.A1_skipStateRegex = tmp;

            this.Ar_prefix = Ar.GetPrefix();
            var Ar_prefix_repr = new string(Array.ConvertAll(this.Ar_prefix, x => (char)sr.Solver.CharSetProvider.GetMin(sr.Solver.ConvertToCharSet(x))));
            this.Ar_skipState = DeltaPlus(Ar_prefix_repr, q0_Ar, out tmp);
            this.Ar_skipStateRegex = tmp;


            //---- seems not useful --- 
            //if (this.A_prefix.Length > 1)
            //{
            //    var first = new List<char>(builder.solver.CharSetProvider.GenerateAllCharacters(
            //        builder.solver.ConvertToCharSet(this.A_prefix_array[0])));
            //    var second = new List<char>(builder.solver.CharSetProvider.GenerateAllCharacters(
            //       builder.solver.ConvertToCharSet(this.A_prefix_array[1])));

            //    ushort[] chars1 = new ushort[Vector<ushort>.Count];
            //    int i1 = 0;
            //    foreach (var c in first)
            //        chars1[i1++] = c;
            //    //fill out the rest of the array with the first element
            //    if (i1 < Vector<ushort>.Count - 1)
            //        while (i1 < Vector<ushort>.Count)
            //            chars1[i1++] = chars1[0];
            //    this.A_First_Vec = new Vector<ushort>(chars1);

            //    ushort[] chars2 = new ushort[Vector<ushort>.Count];
            //    int i2 = 0;
            //    foreach (var c in second)
            //        chars2[i2++] = c;
            //    //fill out the rest of the array with the first element
            //    if (i2 < Vector<ushort>.Count - 1)
            //        while (i2 < Vector<ushort>.Count)
            //            chars2[i2++] = chars2[0];
            //    this.A_Second_Vec = new Vector<ushort>(chars2);
            //}
        }

        /// <summary>
        /// Return the state after the given input.
        /// </summary>
        /// <param name="input">given input</param>
        /// <param name="q">given start state</param>
        /// <param name="regex">regex of returned state</param>
        int DeltaPlus(string input, int q, out SymbolicRegex<S> regex)
        {
            if (string.IsNullOrEmpty(input))
            {
                regex = (q < StateLimit ? state2regex[q] : state2regexExtra[q]);
                return q;
            }
            else
            {
                char c = input[0];
                q = Delta(c, q, out regex);


                for (int i = 1; i < input.Length; i++)
                {
                    c = input[i];

                    int p = 0;

#if INLINE
                    #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                    #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                else
                {
                    #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                    #endregion
#else
                    p = Delta(c, q, out regex);
#endif

                    q = p;
                }
                return q;
            }
        }


        /// <summary>
        /// Compute the target state for source state q and input character c.
        /// All uses of Delta must be inlined for efficiency. 
        /// This is the purpose of the MethodImpl(MethodImplOptions.AggressiveInlining) attribute.
        /// </summary>
        /// <param name="c">input character</param>
        /// <param name="q">state id of source regex</param>
        /// <param name="regex">target regex</param>
        /// <returns>state id of target regex</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int Delta(int c, int q, out SymbolicRegex<S> regex)
        {
            int p;
            #region copy&paste region of the definition of Delta being inlined
            int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
            S atom = atoms[atom_id];
            if (q < StateLimit)
            {
                #region use delta
                int offset = (q * K) + atom_id;
                p = delta[offset];
                if (p == 0)
                {
                    CreateNewTransition(q, atom, offset, out p, out regex);
                }
                else
                {
                    regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                }
                #endregion
            }
            else
            {
                #region use deltaExtra
                int[] q_trans = deltaExtra[q];
                p = q_trans[atom_id];
                if (p == 0)
                {
                    CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                }
                else
                {
                    regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                }
                #endregion
            }
            #endregion
            return p;
        }

        /// <summary>
        /// Critical region for threadsafe applications for defining a new transition from q when q is larger that StateLimit
        /// </summary>
        /// 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateNewTransitionExtra(int q, int atom_id, S atom, int[] q_trans, out int p, out SymbolicRegex<S> regex)
        {
            lock (this)
            {
                //check if meanwhile q_trans[atom_id] has become defined possibly by another thread
                int p1 = q_trans[atom_id];
                if (p1 != 0)
                {
                    p = p1;
                    if (p1 < StateLimit)
                        regex = state2regex[p1];
                    else
                        regex = state2regexExtra[p1];
                }
                else
                {
                    //p is still undefined
                    var q_regex = state2regexExtra[q];
                    var deriv = q_regex.MkDerivative(atom);
                    if (!regex2state.TryGetValue(deriv, out p))
                    {
                        p = nextStateId++;
                        regex2state[deriv] = p;
                        // we know at this point that p >= MaxNrOfStates
                        state2regexExtra[p] = deriv;
                        deltaExtra[p] = new int[K];
                    }
                    q_trans[atom_id] = p;
                    regex = deriv;
                }
            }
        }

        /// <summary>
        /// Critical region for threadsafe applications for defining a new transition
        /// </summary>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateNewTransition(int q, S atom, int offset, out int p, out SymbolicRegex<S> regex)
        {
            lock (this)
            {
                //check if meanwhile delta[offset] has become defined possibly by another thread
                int p1 = delta[offset];
                if (p1 != 0)
                {
                    p = p1;
                    if (p1 < StateLimit)
                        regex = state2regex[p1];
                    else
                        regex = state2regexExtra[p1];
                }
                else
                {
                    var q_regex = state2regex[q];
                    var deriv = q_regex.MkDerivative(atom);
                    if (!regex2state.TryGetValue(deriv, out p))
                    {
                        p = nextStateId++;
                        regex2state[deriv] = p;
                        if (p < StateLimit)
                            state2regex[p] = deriv;
                        else
                            state2regexExtra[p] = deriv;
                        if (p >= StateLimit)
                            deltaExtra[p] = new int[K];
                    }
                    delta[offset] = p;
                    regex = deriv;
                }
            }
        }

        #region safe version of Matches and IsMatch for string input

        /// <summary>
        /// Generate all earliest maximal matches.
        /// <paramref name="input">input string</paramref>
        /// </summary>
        internal Tuple<int, int>[] Matches(string input)
        {
            int k = input.Length;

            //stores the accumulated matches
            List<Tuple<int, int>> matches = new List<Tuple<int, int>>();

            //find the first accepting state
            //initial start position in the input is i = 0
            int i = 0;

            //after a match is found the match_start_boundary becomes 
            //the first postion after the last match
            //enforced when inlcude_overlaps == false
            int match_start_boundary = 0;

            //TBD: dont enforce match_start_boundary when match overlaps are allowed
            bool A_has_nonempty_prefix = (A.FixedPrefix != string.Empty);
            while (true)
            {
                int i_q0_A1;
                //optimize for the case when A starts with a fixed prefix
                i = (A_has_nonempty_prefix ?
                        FindFinalStatePositionOpt(input, i, out i_q0_A1) :
                        FindFinalStatePosition(input, i, out i_q0_A1));

                if (i == k)
                {
                    //end of input has been reached without reaching a final state, so no more matches
                    break;
                }

                int i_start = FindStartPosition(input, i, i_q0_A1);

                int i_end = FindEndPosition(input, i_start);

                var newmatch = new Tuple<int, int>(i_start, i_end + 1 - i_start);
                matches.Add(newmatch);

                //continue matching from the position following last match
                i = i_end + 1;
                match_start_boundary = i;
            }

            return matches.ToArray();
        }

        /// <summary>
        /// Returns true iff the input string matches A.
        /// </summary>
        internal bool IsMatch(string input)
        {
            int k = input.Length;

            if (this.A.containsAnchors)
            {
                #region separate case when A contains anchors
                //TBD prefix optimization  ay still be important here 
                //but the prefix needs to be computed based on A ... but with start anchors removed or treated specially
                if (A2 == null)
                {
                    #region initialize A2 to A.RemoveAnchors()
                    this.A2 = A.ReplaceAnchors();
                    int qA2;
                    if (!regex2state.TryGetValue(this.A2, out qA2))
                    {
                        //the regex does not yet exist
                        qA2 = this.nextStateId++;
                        this.regex2state[this.A2] = qA2;
                    }
                    this.q0_A2 = qA2;
                    if (qA2 >= this.StateLimit)
                    {
                        this.deltaExtra[qA2] = new int[this.K];
                        this.state2regexExtra[qA2] = this.A2;
                    }
                    else
                    {
                        this.state2regex[qA2] = this.A2;
                    }



                    #endregion
                }

                int q = this.q0_A2;
                SymbolicRegex<S> regex = this.A2;
                int i = 0;

                while (i < k)
                {
                    int c = input[i];
                    int p;

#if INLINE
                    #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                    #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                else
                {
                    #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                    #endregion
#else
                    p = Delta(c, q, out regex);
#endif

                    if (regex == regex.builder.dotStar) //(regex.IsEverything)
                    {
                        //the input is accepted no matter how the input continues
                        return true;
                    }
                    if (regex == regex.builder.nothing) //(regex.IsNothing)
                    {
                        //the input is rejected no matter how the input continues
                        return false;
                    }

                    //continue from the target state
                    q = p;
                    i += 1;
                }
                return regex.IsNullable(i==0, true);
                #endregion
            }
            else
            {
                //reuse A1
                int i;
                int i_q0;
                if (A.FixedPrefix != string.Empty)
                {
                    i = FindFinalStatePositionOpt(input, 0, out i_q0);
                }
                else
                {
                    i = FindFinalStatePosition(input, 0, out i_q0);
                }
                if (i == k)
                {
                    //the search for final state exceeded the input, so final state was not found
                    return false;
                }
                else
                {
                    //since A has no anchors the pattern is really .*A.*
                    //thus if input[0...i] is in L(.*A) then input is in L(.*A.*)
                    return true;
                }
            }
        }

        /// <summary>
        /// Find match end position using A, end position is known to exist.
        /// </summary>
        /// <param name="input">input array</param>
        /// <param name="i">start position</param>
        /// <returns></returns>
        private int FindEndPosition(string input, int i)
        {
            int k = input.Length;
            int i_end = k;
            int q = q0_A;
            while (i < k)
            {
                SymbolicRegex<S> regex;
                int c = input[i];
                int p;

                //TBD: anchors

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                else
                {
                #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif


                if (regex.isNullable)
                {
                    //accepting state has been reached
                    //record the position 
                    i_end = i;
                }
                else if (regex == builder.nothing)
                {
                    //nonaccepting sink state (deadend) has been reached in A
                    //so the match ended when the last i_end was updated
                    break;
                }
                q = p;
                i += 1;
            }
            if (i_end == k)
                throw new AutomataException(AutomataExceptionKind.InternalError);
            return i_end;
        }

        /// <summary>
        /// Walk back in reverse using Ar to find the start position of match, start position is known to exist.
        /// </summary>
        /// <param name="input">the input array</param>
        /// <param name="i">position to start walking back from, i points at the last character of the match</param>
        /// <param name="match_start_boundary">do not pass this boundary when walking back</param>
        /// <returns></returns>
        private int FindStartPosition(string input, int i, int match_start_boundary)
        {
            int q = q0_Ar;
            SymbolicRegex<S> regex = null;
            //A_r may have a fixed sequence
            if (this.Ar_prefix.Length > 0)
            {
                //skip back the prefix portion of Ar
                q = this.Ar_skipState;
                regex = this.Ar_skipStateRegex;
                i = i - this.Ar_prefix.Length;
            }
            if (i == -1)
            {
                //we reached the beginning of the input, thus the state q must be accepting
                if (!regex.isNullable)
                    throw new AutomataException(AutomataExceptionKind.InternalError);
                return 0;
            }

            int last_start = -1;
            if (regex != null && regex.isNullable)
            {
                //the whole prefix of Ar was in reverse a prefix of A
                last_start = i + 1;
            }

            //walk back to the accepting state of Ar
            int p;
            int c;
            while (i >= match_start_boundary)
            {
                //observe that the input is reversed 
                //so input[k-1] is the first character 
                //and input[0] is the last character
                //TBD: anchors
                c = input[i];

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                else
                {
                #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //earliest start point so far
                    //this must happen at some point 
                    //or else A1 would not have reached a 
                    //final state after match_start_boundary
                    last_start = i;
                    //TBD: under some conditions we can break here
                    //break;
                }
                else if (regex == this.builder.nothing)
                {
                    //the previous i_start was in fact the earliest
                    break;
                }
                q = p;
                i -= 1;
            }
            if (last_start == -1)
                throw new AutomataException(AutomataExceptionKind.InternalError);
            return last_start;
        }

        /// <summary>
        /// Return the position of the last character that leads to a final state in A1
        /// </summary>
        /// <param name="input">given input array</param>
        /// <param name="i">start position</param>
        /// <param name="i_q0">last position the initial state of A1 was visited</param>
        /// <returns></returns>
        private int FindFinalStatePosition(string input, int i, out int i_q0)
        {
            int k = input.Length;
            int q = q0_A1;
            int i_q0_A1 = i;

            while (i < k)
            {
                if (q == q0_A1)
                {
                    i = IndexOfStartset(input, i);

                    if (i == -1)
                    {
                        i_q0 = i_q0_A1;
                        return k;
                    }
                    i_q0_A1 = i;
                }

                //TBD: anchors
                SymbolicRegex<S> regex;
                int c = input[i];
                int p;

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                else
                {
                #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //p is a final state so match has been found
                    break;
                }
                else if (regex == regex.builder.nothing)
                {
                    //p is a deadend state so any further search is meaningless
                    i_q0 = i_q0_A1;
                    return k;
                }

                //continue from the target state
                q = p;
                i += 1;
            }
            i_q0 = i_q0_A1;
            return i;
        }

        /// <summary>
        /// FindFinalState optimized for the case when A starts with a fixed prefix
        /// </summary>
        private int FindFinalStatePositionOpt(string input, int i, out int i_q0)
        {
            int k = input.Length;
            int q = q0_A1;
            int i_q0_A1 = i;
            var prefix = this.A_prefix;
            //it is important to use Ordinal/OrdinalIgnoreCase to avoid culture dependent semantics of IndexOf
            StringComparison comparison = (this.A_fixedPrefix_ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            while (i < k)
            {
                SymbolicRegex<S> regex = null;

                // ++++ the following prefix optimization can be commented out without affecting correctness ++++
                // but this optimization has a huge perfomance boost when fixed prefix exists .... in the order of 10x
                //
                #region prefix optimization 
                //stay in the initial state if the prefix does not match
                //thus advance the current position to the 
                //first position where the prefix does match
                if (q == q0_A1)
                {
                    i_q0_A1 = i;

                    //i = IndexOf(input, prefix, i, this.A_fixedPrefix_ignoreCase);
                    i = input.IndexOf(prefix, i, comparison);

                    if (i == -1)
                    {
                        //if a matching position does not exist then IndexOf returns -1
                        //so set i = k to match the while loop behavior
                        i = k;
                        break;
                    }
                    else
                    {
                        //compute the end state for the A prefix
                        //skip directly to the resulting state
                        // --- i.e. does the loop ---
                        //for (int j = 0; j < prefix.Length; j++)
                        //    q = Delta(prefix[j], q, out regex);
                        // ---
                        q = this.A1_skipState;
                        regex = this.A1_skipStateRegex;

                        //skip the prefix
                        i = i + prefix.Length;
                        if (regex.isNullable)
                        {
                            i_q0 = i_q0_A1;
                            //return the last position of the match
                            return i - 1;
                        }
                        if (i == k)
                        {
                            i_q0 = i_q0_A1;
                            return k;
                        }
                    }
                }
                #endregion

                //TBD: anchors
                int c = input[i];
                int p;

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                else
                {
                #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //p is a final state so match has been found
                    break;
                }
                else if (regex == regex.builder.nothing)
                {
                    i_q0 = i_q0_A1;
                    //p is a deadend state so any further saerch is meaningless
                    return k;
                }

                //continue from the target state
                q = p;
                i += 1;
            }
            i_q0 = i_q0_A1;
            return i;
        }

        #endregion

#if UNSAFE

        #region unsafe version of Matches for string input

        /// <summary>
        /// Generate all earliest maximal matches. We know that k is at least 2. Unsafe version of Matches.
        /// <paramref name="input">pointer to input string</paramref>
        /// </summary>
        unsafe internal Tuple<int, int>[] Matches_(string input)
        {
            int k = input.Length;
            //stores the accumulated matches
            List<Tuple<int, int>> matches = new List<Tuple<int, int>>();

            //find the first accepting state
            //initial start position in the input is i = 0
            int i = 0;

            //after a match is found the match_start_boundary becomes 
            //the first postion after the last match
            //enforced when inlcude_overlaps == false
            int match_start_boundary = 0;

            //TBD: dont enforce match_start_boundary when match overlaps are allowed
            bool A_has_nonempty_prefix = (A.FixedPrefix != string.Empty);
            fixed (char* inputp = input)
                while (true)
                {
                    int i_q0_A1;
                    if (A_has_nonempty_prefix)
                    {
                        i = FindFinalStatePositionOpt_(input, i, out i_q0_A1);
                    }
                    else
                    {
                        i = FindFinalStatePosition_(inputp, k, i, out i_q0_A1);
                    }

                    if (i == k)
                    {
                        //end of input has been reached without reaching a final state, so no more matches
                        break;
                    }

                    int i_start = FindStartPosition_(inputp, i, i_q0_A1);

                    int i_end = FindEndPosition_(inputp, k, i_start);

                    var newmatch = new Tuple<int, int>(i_start, i_end + 1 - i_start);
                    matches.Add(newmatch);

                    //continue matching from the position following last match
                    i = i_end + 1;
                    match_start_boundary = i;
                }

            return matches.ToArray();
        }

        /// <summary>
        /// Return the position of the last character that leads to a final state in A1
        /// </summary>
        /// <param name="inputp">given input string</param>
        /// <param name="k">length of input</param>
        /// <param name="i">start position</param>
        /// <param name="i_q0">last position the initial state of A1 was visited</param>
        /// <returns></returns>
        unsafe private int FindFinalStatePosition_(char* inputp, int k, int i, out int i_q0)
        {
            int q = q0_A1;
            int i_q0_A1 = i;
            while (i < k)
            {
                if (q == q0_A1)
                {
                    if (this.A_StartSet_Vec == null)
                    {
                        i = IndexOfStartset_(inputp, k, i);
                    }
                    else if (A_StartSet_Vec.Length == 1)
                    {
                        i = VectorizedIndexOf.UnsafeIndexOf(inputp, k, i, this.A_StartSet, A_StartSet_Vec[0]);
                    }
                    else
                    {
                        i = VectorizedIndexOf.UnsafeIndexOf(inputp, k, i, this.A_StartSet, A_StartSet_Vec);
                    }

                    if (i == -1)
                    {
                        i_q0 = i_q0_A1;
                        return k;
                    }
                    i_q0_A1 = i;
                }

                //TBD: anchors
                SymbolicRegex<S> regex;
                int c = inputp[i];
                int p;

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                    int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                    S atom = atoms[atom_id];
                    if (q < StateLimit)
                    {
                #region use delta
                        int offset = (q * K) + atom_id;
                        p = delta[offset];
                        if (p == 0)
                        {
                            CreateNewTransition(q, atom, offset, out p, out regex);
                        }
                        else
                        {
                            regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                        }
                #endregion
                    }
                    else
                    {
                #region use deltaExtra
                        int[] q_trans = deltaExtra[q];
                        p = q_trans[atom_id];
                        if (p == 0)
                        {
                            CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                        }
                        else
                        {
                            regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                        }
                #endregion
                    }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //p is a final state so match has been found
                    break;
                }
                else if (regex == regex.builder.nothing)
                {
                    //p is a deadend state so any further search is meaningless
                    i_q0 = i_q0_A1;
                    return k;
                }

                //continue from the target state
                q = p;
                i += 1;
            }
            i_q0 = i_q0_A1;
            return i;
        }

        /// <summary>
        /// FindFinalState optimized for the case when A starts with a fixed prefix and does not ignore case
        /// </summary>
        unsafe private int FindFinalStatePositionOpt_(string input, int i, out int i_q0)
        {
            int q = q0_A1;
            int i_q0_A1 = i;
            var A_prefix_length = this.A_prefix.Length;
            //it is important to use Ordinal/OrdinalIgnoreCase to avoid culture dependent semantics of IndexOf
            StringComparison comparison = (this.A_fixedPrefix_ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            int k = input.Length;
            fixed (char* inputp = input)
            while (i < k)
            {
                SymbolicRegex<S> regex = null;

                #region prefix optimization 
                //stay in the initial state if the prefix does not match
                //thus advance the current position to the 
                //first position where the prefix does match
                if (q == q0_A1)
                {
                    i_q0_A1 = i;

                    if (this.A_fixedPrefix_ignoreCase)
                        i = input.IndexOf(A_prefix, i, comparison);
                    else
                        i = IndexOfStartPrefix_(inputp, k, i);

                    if (i == -1)
                    {
                        //if a matching position does not exist then IndexOf returns -1
                        //so set i = k to match the while loop behavior
                        i = k;
                        break;
                    }
                    else
                    {
                        //compute the end state for the A prefix
                        //skip directly to the resulting state
                        // --- i.e. does the loop ---
                        //for (int j = 0; j < prefix.Length; j++)
                        //    q = Delta(prefix[j], q, out regex);
                        // ---
                        q = this.A1_skipState;
                        regex = this.A1_skipStateRegex;

                        //skip the prefix
                        i = i + A_prefix_length;
                        if (regex.isNullable)
                        {
                            i_q0 = i_q0_A1;
                            //return the last position of the match
                            return i - 1;
                        }
                        if (i == k)
                        {
                            i_q0 = i_q0_A1;
                            return k;
                        }
                    }
                }
                #endregion

                //TBD: anchors
                int c = inputp[i];
                int p;

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                    int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                    S atom = atoms[atom_id];
                    if (q < StateLimit)
                    {
                #region use delta
                        int offset = (q * K) + atom_id;
                        p = delta[offset];
                        if (p == 0)
                        {
                            CreateNewTransition(q, atom, offset, out p, out regex);
                        }
                        else
                        {
                            regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                        }
                #endregion
                    }
                    else
                    {
                #region use deltaExtra
                        int[] q_trans = deltaExtra[q];
                        p = q_trans[atom_id];
                        if (p == 0)
                        {
                            CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                        }
                        else
                        {
                            regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                        }
                #endregion
                    }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //p is a final state so match has been found
                    break;
                }
                else if (regex == regex.builder.nothing)
                {
                    i_q0 = i_q0_A1;
                    //p is a deadend state so any further search is meaningless
                    return k;
                }

                //continue from the target state
                q = p;
                i += 1;
            }
            i_q0 = i_q0_A1;
            return i;
        }

        /// <summary>
        /// Walk back in reverse using Ar to find the start position of match, start position is known to exist.
        /// </summary>
        /// <param name="input">the input array</param>
        /// <param name="i">position to start walking back from, i points at the last character of the match</param>
        /// <param name="match_start_boundary">do not pass this boundary when walking back</param>
        /// <returns></returns>
        unsafe private int FindStartPosition_(char* input, int i, int match_start_boundary)
        {
            int q = q0_Ar;
            SymbolicRegex<S> regex = null;
            //A_r may have a fixed sequence
            if (this.Ar_prefix.Length > 0)
            {
                //skip back the prefix portion of Ar
                q = this.Ar_skipState;
                regex = this.Ar_skipStateRegex;
                i = i - this.Ar_prefix.Length;
            }
            if (i == -1)
            {
                //we reached the beginning of the input, thus the state q must be accepting
                if (!regex.isNullable)
                    throw new AutomataException(AutomataExceptionKind.InternalError);
                return 0;
            }

            int last_start = -1;
            if (regex != null && regex.isNullable)
            {
                //the whole prefix of Ar was in reverse a prefix of A
                last_start = i + 1;
            }

            //walk back to the accepting state of Ar
            int p;
            int c;
            while (i >= match_start_boundary)
            {
                //observe that the input is reversed 
                //so input[k-1] is the first character 
                //and input[0] is the last character
                //TBD: anchors
                c = input[i];

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                else
                {
                #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //earliest start point so far
                    //this must happen at some point 
                    //or else A1 would not have reached a 
                    //final state after match_start_boundary
                    last_start = i;
                    //TBD: under some conditions we can break here
                    //break;
                }
                else if (regex == this.builder.nothing)
                {
                    //the previous i_start was in fact the earliest
                    break;
                }
                q = p;
                i -= 1;
            }
            if (last_start == -1)
                throw new AutomataException(AutomataExceptionKind.InternalError);
            return last_start;
        }

        /// <summary>
        /// Find match end position using A, end position is known to exist.
        /// </summary>
        /// <param name="input">input array</param>
        /// <param name="k">length of input</param>
        /// <param name="i">start position</param>
        /// <returns></returns>
        unsafe private int FindEndPosition_(char* input, int k, int i)
        {
            int i_end = k;
            int q = q0_A;
            while (i < k)
            {
                SymbolicRegex<S> regex;
                int c = input[i];
                int p;

                //TBD: anchors

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                else
                {
                #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif


                if (regex.isNullable)
                {
                    //accepting state has been reached
                    //record the position 
                    i_end = i;
                }
                else if (regex == builder.nothing)
                {
                    //nonaccepting sink state (deadend) has been reached in A
                    //so the match ended when the last i_end was updated
                    break;
                }
                q = p;
                i += 1;
            }
            if (i_end == k)
                throw new AutomataException(AutomataExceptionKind.InternalError);
            return i_end;
        }

        #endregion

#endif

        #region Specialized IndexOf
        /// <summary>
        ///  Find first occurrence of startset element in input starting from index i.
        /// </summary>
        /// <param name="input">input string to search in</param>
        /// <param name="i">the start index in input to search from</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int IndexOfStartset(string input, int i)
        {
            int k = input.Length;
            while (i < k) 
            {
                var input_i = input[i];
                if (input_i < A_StartSet.precomputed.Length ? A_StartSet.precomputed[input_i] : A_StartSet.bst.Find(input_i) == 1)
                    break;
                else
                    i += 1;
            }
            if (i == k)
                return -1;
            else
                return i;
        }

        /// <summary>
        ///  Find first occurrence of startset element in input starting from index i.
        /// </summary>
        /// <param name="input">input string to search in</param>
        /// <param name="i">the start index in input to search from</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int IndexOfStartsetUTF8(byte[] input, int i, ref int surrogate_codepoint)
        {
            int k = input.Length;
            int step = 1;
            int codepoint = 0;
            while (i < k)
            {
                int c = input[i];
                if (c > 0x7F)
                {
                    UTF8Encoding.DecodeNextNonASCII(input, i, out step, out codepoint);
                    if (codepoint > 0xFFFF)
                    {
                        throw new NotImplementedException("surrogate pairs");
                    }
                    else
                    {
                        c = codepoint;
                    }
                }

                if (c < A_StartSet.precomputed.Length ? A_StartSet.precomputed[c] : A_StartSet.bst.Find(c) == 1)
                    break;
                else
                {
                    i += step; 
                }
            }
            if (i == k)
                return -1;
            else
                return i;
        }

        /// <summary>
        ///  Find first occurrence of startset element in input starting from index i.
        /// </summary>
        /// <param name="input">input string to search in</param>
        /// <param name="k">length of the input</param>
        /// <param name="i">the start index in input to search from</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe int IndexOfStartset_(char* input, int k, int i)
        {
            while (i < k)
            {
                var input_i = input[i];
                if (input_i < A_StartSet.precomputed.Length ? A_StartSet.precomputed[input_i] : A_StartSet.bst.Find(input_i) == 1)
                    break;
                else
                    i += 1;
            }
            if (i == k)
                return -1;
            else
                return i;
        }

        /// <summary>
        ///  Find first occurrence of value in input starting from index i.
        /// </summary>
        /// <param name="input">input array to search in</param>
        /// <param name="value">nonempty subarray that is searched for</param>
        /// <param name="i">the search start index in input</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int IndexOf(byte[] input, byte[] value, int i)
        {
            int n = value.Length;
            int k = (input.Length - n) + 1;
            while (i < k)
            {
                i = Array.IndexOf<byte>(input, value[0], i);
                if (i == -1)
                    return -1;
                int j = 1;
                while (j < n && input[i + j] == value[j])
                    j += 1;
                if (j == n)
                    return i;
                i += 1;
            }
            return -1;
        }

        /// <summary>
        ///  Find first occurrence of byte in input starting from index i that maps to true by the predicate.
        /// </summary>
        /// <param name="input">input array to search in</param>
        /// <param name="pred">boolean array of size 256 telling which bytes to match</param>
        /// <param name="i">the search start index in input</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int IndexOf(byte[] input, bool[] pred, int i)
        {
            int k = input.Length;
            while (i < k && !pred[input[i]])
                i += 1;
            return (i == k ? -1 : i);
        }

        /// <summary>
        ///  Find first occurrence of s in input starting from index i.
        ///  This method is called when A has nonemmpty prefix and ingorecase is false
        /// </summary>
        /// <param name="input">input string to search in</param>
        /// <param name="k">length of input string</param>
        /// <param name="i">the start index in input</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe int IndexOfStartPrefix_(char* input, int k, int i)
        {
            int l = this.A_prefix.Length;
            int k1 = k - l + 1;
            var vec = A_StartSet_Vec[0];
            fixed (char* p = this.A_prefix)
            {
                while (i < k1)
                {
                    i = VectorizedIndexOf.UnsafeIndexOf1(input, k, i, p[0], vec);

                    if (i == -1)
                        return -1;
                    int j = 1;
                    while (j < l && input[i + j] == p[j])
                        j += 1;
                    if (j == l)
                        return i;

                    i += 1;
                }
            }
            return -1;
        }

        #endregion

        #region Matches that uses UTF-8 encoded byte array as input

        /// <summary>
        /// Generate all earliest maximal matches.
        /// <paramref name="input">pointer to input string</paramref>
        /// </summary>
        internal Tuple<int, int>[] MatchesUTF8(byte[] input)
        {
            int k = input.Length;

            //stores the accumulated matches
            List<Tuple<int, int>> matches = new List<Tuple<int, int>>();

            //find the first accepting state
            //initial start position in the input is i = 0
            int i = 0;

            //after a match is found the match_start_boundary becomes 
            //the first postion after the last match
            //enforced when inlcude_overlaps == false
            int match_start_boundary = 0;

            int surrogate_codepoint = 0;   

            //TBD: dont enforce match_start_boundary when match overlaps are allowed
            bool A_has_nonempty_prefix = (A.FixedPrefix != string.Empty);
            while (true)
            {
                int i_q0_A1;
                //TBD: optimize for the case when A starts with a fixed prefix
                i = FindFinalStatePositionUTF8(input, i, ref surrogate_codepoint, out i_q0_A1);

                if (i == k)
                {
                    //end of input has been reached without reaching a final state, so no more matches
                    break;
                }

                int i_start = FindStartPositionUTF8(input, i, ref surrogate_codepoint, i_q0_A1);

                int i_end = FindEndPositionUTF8(input, i_start, ref surrogate_codepoint);

                var newmatch = new Tuple<int, int>(i_start, i_end + 1 - i_start);
                matches.Add(newmatch);

                //continue matching from the position following last match
                i = i_end + 1;
                match_start_boundary = i;
            }

            return matches.ToArray();
        }

        /// <summary>
        /// Find match end position using A, end position is known to exist.
        /// </summary>
        /// <param name="input">input array</param>
        /// <param name="i">start position</param>
        /// <param name="surrogate_codepoint">surrogate codepoint</param>
        /// <returns></returns>
        private int FindEndPositionUTF8(byte[] input, int i, ref int surrogate_codepoint)
        {
            int k = input.Length;
            int i_end = k;
            int q = q0_A;
            int step = 0;
            int codepoint = 0;
            while (i < k)
            {
                SymbolicRegex<S> regex;

                ushort c;
                #region c = current UTF16 character
                if (surrogate_codepoint == 0)
                {
                    c = input[i];
                    if (c > 0x7F)
                    {
                        int x;
                        UTF8Encoding.DecodeNextNonASCII(input, i, out x, out codepoint);
                        if (codepoint > 0xFFFF)
                        {
                            surrogate_codepoint = codepoint;
                            c = UTF8Encoding.HighSurrogate(codepoint);
                            //do not increment i yet because L is pending
                            step = 0;
                        }
                        else
                        {
                            c = (ushort)codepoint;
                            //step is either 2 or 3, i.e. either 2 or 3 UTF-8-byte encoding
                            step = x;
                        }
                    }
                }
                else
                {
                    c = UTF8Encoding.LowSurrogate(surrogate_codepoint);
                    //reset the surrogate_codepoint
                    surrogate_codepoint = 0;
                    //increment i by 4 since low surrogate has now been read
                    step = 4;
                }
                #endregion

                int p;

                //TBD: anchors

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                    #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                else
                {
                    #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif


                if (regex.isNullable)
                {
                    //accepting state has been reached
                    //record the position 
                    i_end = i;
                }
                else if (regex == builder.nothing)
                {
                    //nonaccepting sink state (deadend) has been reached in A
                    //so the match ended when the last i_end was updated
                    break;
                }
                q = p;
                if (c > 0x7F)
                    i += step;
                else
                    i += 1;
            }
            if (i_end == k)
                throw new AutomataException(AutomataExceptionKind.InternalError);
            return i_end;
        }

        /// <summary>
        /// Walk back in reverse using Ar to find the start position of match, start position is known to exist.
        /// </summary>
        /// <param name="input">the input array</param>
        /// <param name="i">position to start walking back from, i points at the last character of the match</param>
        /// <param name="match_start_boundary">do not pass this boundary when walking back</param>
        /// <param name="surrogate_codepoint">surrogate codepoint</param>
        /// <returns></returns>
        private int FindStartPositionUTF8(byte[] input, int i, ref int surrogate_codepoint, int match_start_boundary)
        {
            int q = q0_Ar;
            SymbolicRegex<S> regex = null;
            //A_r may have a fixed sequence
            if (this.Ar_prefix.Length > 0)
            {
                //skip back the prefix portion of Ar
                q = this.Ar_skipState;
                regex = this.Ar_skipStateRegex;
                i = i - this.Ar_prefix.Length;
            }
            if (i == -1)
            {
                //we reached the beginning of the input, thus the state q must be accepting
                if (!regex.isNullable)
                    throw new AutomataException(AutomataExceptionKind.InternalError);
                return 0;
            }

            int last_start = -1;
            if (regex != null && regex.isNullable)
            {
                //the whole prefix of Ar was in reverse a prefix of A
                last_start = i + 1;
            }

            //walk back to the accepting state of Ar
            int p;
            ushort c;
            int step = 0;
            int codepoint;
            while (i >= match_start_boundary)
            {
                //observe that the input is reversed 
                //so input[k-1] is the first character 
                //and input[0] is the last character
                //but encoding is not reversed
                //TBD: anchors

                #region c = current UTF16 character
                if (surrogate_codepoint == 0)
                {
                    //not in the middel of surrogate codepoint 
                    c = input[i];
                    if (c > 0x7F)
                    {
                        int _;
                        UTF8Encoding.DecodeNextNonASCII(input, i, out _, out codepoint);
                        if (codepoint > 0xFFFF)
                        {
                            //given codepoint = ((H - 0xD800) * 0x400) + (L - 0xDC00) + 0x10000
                            surrogate_codepoint = codepoint;
                            //compute c = L (going backwards) 
                            c = (ushort)(((surrogate_codepoint - 0x10000) & 0x3FF) | 0xDC00);
                        }
                        else
                        {
                            c = (ushort)codepoint;
                        }
                    }
                }
                else
                {
                    //given surrogate_codepoint = ((H - 0xD800) * 0x400) + (L - 0xDC00) + 0x10000
                    //compute c = H (going backwards)
                    c = (ushort)(((surrogate_codepoint - 0x10000) >> 10) | 0xD800);
                    //reset the surrogate codepoint 
                    surrogate_codepoint = 0;
                }
                #endregion

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                    #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                else
                {
                    #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //earliest start point so far
                    //this must happen at some point 
                    //or else A1 would not have reached a 
                    //final state after match_start_boundary
                    last_start = i;
                    //TBD: under some conditions we can break here
                    //break;
                }
                else if (regex == this.builder.nothing)
                {
                    //the previous i_start was in fact the earliest
                    surrogate_codepoint = 0;
                    break;
                }
                if (surrogate_codepoint == 0)
                {
                    i = i - 1;
                    //step back to the previous input, /while input[i] is not a start-byte take a step back
                    //check (0x7F < b && b < 0xC0) imples that 0111.1111 < b < 1100.0000
                    //so b cannot be ascii 0xxx.xxxx or startbyte 110x.xxxx or 1110.xxxx or 1111.0xxx
                    while ((i >= match_start_boundary) && (0x7F < input[i] && input[i] < 0xC0))
                        i = i - 1;
                }
                q = p;
            }
            if (last_start == -1)
                throw new AutomataException(AutomataExceptionKind.InternalError);
            return last_start;
        }

        /// <summary>
        /// Return the position of the last character that leads to a final state in A1
        /// </summary>
        /// <param name="input">given input array</param>
        /// <param name="i">start position</param>
        /// <param name="i_q0">last position the initial state of A1 was visited</param>
        /// <param name="surrogate_codepoint">surrogate codepoint</param>
        /// <returns></returns>
        private int FindFinalStatePositionUTF8(byte[] input, int i, ref int surrogate_codepoint,  out int i_q0)
        {
            int k = input.Length;
            int q = q0_A1;
            int i_q0_A1 = i;
            int step = 0;
            int codepoint;
            SymbolicRegex<S> regex;
            bool prefix_optimize = (!this.A_fixedPrefix_ignoreCase) && this.A_prefixUTF8.Length > 1;
            while (i < k)
            {
                if (q == q0_A1)
                {
                    if (prefix_optimize)
                    {
                        #region prefix optimization when A has a fixed prefix and is case-sensitive
                        //stay in the initial state if the prefix does not match
                        //thus advance the current position to the 
                        //first position where the prefix does match
                        i_q0_A1 = i;

                        i = VectorizedIndexOf.IndexOfByteSeq(input, i, this.A_prefixUTF8, this.A_prefixUTF8_first_byte);

                        if (i == -1)
                        {
                            //if a matching position does not exist then IndexOf returns -1
                            //so set i = k to match the while loop behavior
                            i = k;
                            break;
                        }
                        else
                        {
                            //compute the end state for the A prefix
                            //skip directly to the resulting state
                            // --- i.e. do the loop ---
                            //for (int j = 0; j < prefix.Length; j++)
                            //    q = Delta(prefix[j], q, out regex);
                            // ---
                            q = this.A1_skipState;
                            regex = this.A1_skipStateRegex;

                            //skip the prefix
                            i = i + this.A_prefixUTF8.Length;
                            if (regex.isNullable)
                            {
                                i_q0 = i_q0_A1;
                                //return the last position of the match
                                //make sure to step back to the start byte
                                i = i - 1;
                                //while input[i] is not a start-byte take a step back
                                while (0x7F < input[i] && input[i] < 0xC0)
                                    i = i - 1;
                            }
                            if (i == k)
                            {
                                i_q0 = i_q0_A1;
                                return k;
                            }
                        }
                        #endregion
                    }
                    else
                    {
                        i = (this.A_prefixUTF8.Length == 0 ?
                            IndexOfStartsetUTF8(input, i, ref surrogate_codepoint) :
                            VectorizedIndexOf.IndexOfByte(input, i, this.A_prefixUTF8[0], this.A_prefixUTF8_first_byte));

                        if (i == -1)
                        {
                            i_q0 = i_q0_A1;
                            return k;
                        }
                        i_q0_A1 = i;
                    }
                }

                ushort c;

                #region c = current UTF16 character
                if (surrogate_codepoint == 0)
                {
                    c = input[i];
                    if (c > 0x7F)
                    {
                        int x;
                        UTF8Encoding.DecodeNextNonASCII(input, i, out x, out codepoint);
                        if (codepoint > 0xFFFF)
                        {
                            //given codepoint = ((H - 0xD800) * 0x400) + (L - 0xDC00) + 0x10000
                            surrogate_codepoint = codepoint;
                            //compute c = H 
                            c = (ushort)(((codepoint - 0x10000) >> 10) | 0xD800);
                            //do not increment i yet because L is pending
                            step = 0;
                        }
                        else
                        {
                            c = (ushort)codepoint;
                            //step is either 2 or 3, i.e. either 2 or 3 UTF-8-byte encoding
                            step = x;
                        }
                    }
                }
                else
                {
                    //given surrogate_codepoint = ((H - 0xD800) * 0x400) + (L - 0xDC00) + 0x10000
                    //compute c = L 
                    c = (ushort)(((surrogate_codepoint - 0x10000) & 0x3FF) | 0xDC00);
                    //reset the surrogate_codepoint
                    surrogate_codepoint = 0;
                    //increment i by 4 since low surrogate has now been read
                    step = 4;
                }
                #endregion


                //TBD: anchors
                int p;

#if INLINE
                #region copy&paste region of the definition of Delta being inlined
                int atom_id = (dt.precomputed.Length > c ? dt.precomputed[c] : dt.bst.Find(c));
                S atom = atoms[atom_id];
                if (q < StateLimit)
                {
                    #region use delta
                    int offset = (q * K) + atom_id;
                    p = delta[offset];
                    if (p == 0)
                    {
                        CreateNewTransition(q, atom, offset, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                else
                {
                    #region use deltaExtra
                    int[] q_trans = deltaExtra[q];
                    p = q_trans[atom_id];
                    if (p == 0)
                    {
                        CreateNewTransitionExtra(q, atom_id, atom, q_trans, out p, out regex);
                    }
                    else
                    {
                        regex = (p < StateLimit ? state2regex[p] : state2regexExtra[p]);
                    }
                    #endregion
                }
                #endregion
#else
                p = Delta(c, q, out regex);
#endif

                if (regex.isNullable)
                {
                    //p is a final state so match has been found
                    break;
                }
                else if (regex == regex.builder.nothing)
                {
                    //p is a deadend state so any further search is meaningless
                    i_q0 = i_q0_A1;
                    return k;
                }

                //continue from the target state
                q = p;
                if (c > 0x7F)
                    i += step;
                else
                    i += 1;
            }
            i_q0 = i_q0_A1;
            return i;
        }

        #endregion
    }
}
