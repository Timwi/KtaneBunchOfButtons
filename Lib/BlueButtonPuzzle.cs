using System;
using System.Collections.Generic;
using System.Linq;

namespace BunchOfButtonsLib
{
    public class BlueButtonPuzzle
    {
        // PUBLIC
        public PolyominoPlacement[] PolyominoSequence { get; private set; }
        public int[] PolyominoSequenceColors { get; private set; }
        public int[] PolyominoGrid { get; private set; }
        public int[] ColorStageColors { get; private set; }
        public int[] EquationOffsets { get; private set; }
        public string Word { get; private set; }
        public int[] Suits { get; private set; }
        public int[] Jumps { get; private set; }

        public const int GridWidth = 5;
        public const int GridHeight = 4;

        public static BlueButtonPuzzle GeneratePuzzle(int seed)
        {
            var rnd = new Random(seed);

            tryAgain:
            var word = WordLists.FiveLetters[rnd.Next(0, WordLists.FiveLetters.Length)];
            var result = GenerateRandomPolyominoSolution(rnd, word);
            if (result == null)
                goto tryAgain;

            var (generatedGrid, generatedPolys, generatedJumps) = result.Value;
            generatedPolys.Shuffle(rnd);

            // Without loss of generality, assume the first polyomino is a given
            var givenPolyominoPlacement = generatedPolys[0];
            var givenGrid = new int?[GridWidth * GridHeight];
            foreach (var cell in givenPolyominoPlacement.Polyomino.Cells)
                givenGrid[givenPolyominoPlacement.Place.AddWrap(cell).Index] = 0;
            var possiblePlacements = GetAllPolyominoPlacements()
                .Where(pl =>
                    pl.Polyomino.Cells.All(c => givenGrid[pl.Place.AddWrap(c).Index] == null) &&
                    generatedPolys.Skip(1).Any(tup => tup.Polyomino == pl.Polyomino))
                .ToArray();

            IEnumerable<(int[] solution, PolyominoPlacement[] polys)> GenerateSolutions(IEnumerable<(Polyomino one, Polyomino two)> noAllowTouch)
            {
                var grid = givenGrid.ToArray();
                var placements = possiblePlacements.ToList();
                foreach (var (one, two) in noAllowTouch)
                    if (one == givenPolyominoPlacement.Polyomino)
                        placements.RemoveAll(pl => pl.Polyomino == two && pl.Touches(givenPolyominoPlacement));
                    else if (two == givenPolyominoPlacement.Polyomino)
                        placements.RemoveAll(pl => pl.Polyomino == one && pl.Touches(givenPolyominoPlacement));

                return SolvePolyominoPuzzle(grid, 1, placements, noAllowTouch);
            }

            IEnumerable<(Polyomino one, Polyomino two)[]> findTouchConstraints((Polyomino one, Polyomino two)[] sofar)
            {
                var solutions = GenerateSolutions(sofar).Take(2).ToArray();

                // Puzzle unique!
                if (solutions.Length == 1)
                {
                    yield return sofar;
                    yield break;
                }

                // Find a wrong solution
                var (_, wrongAllPolys) = solutions.First(s => s.polys.Any(sPl => !generatedPolys.Contains(sPl)));
                // Find all wrong polyominoes in this wrong solution
                var wrongPolys = wrongAllPolys.Where(sPl => !generatedPolys.Contains(sPl)).ToArray();
                // Find all polyominoes that:
                //  • touch a wrong polyomino in the wrong solution
                //  • do not touch the corresponding correct polyomino in the correct solution
                //  • aren’t already in ‘sofar’
                var touchingPolys = wrongPolys.SelectMany(wPl => wrongAllPolys
                    .Where(owPl => owPl.Touches(wPl) && !generatedPolys.First(gPl => gPl.Polyomino == wPl.Polyomino).Touches(generatedPolys.First(gPl => gPl.Polyomino == owPl.Polyomino)))
                    .Select(owPl => (one: wPl.Polyomino, two: owPl.Polyomino)))
                    .Where(tup => !sofar.Any(tup2 => tup.one == tup2.one || tup.one == tup2.two || tup.two == tup2.one || tup.two == tup2.two))
                    .ToArray();

                foreach (var tup in touchingPolys)
                    foreach (var solution in findTouchConstraints(sofar.Concat(new[] { tup }).ToArray()))
                        yield return solution;
            }
            var notAllowedToTouch = findTouchConstraints(new (Polyomino one, Polyomino two)[0]).FirstOrDefault();
            if (notAllowedToTouch == null)
                goto tryAgain;

            // Assign the polyominoes colors
            var colors = new int[generatedPolys.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                var availableColors = Enumerable.Range(0, 6).ToList();
                var already = generatedPolys.Take(i).IndexOf(prev => notAllowedToTouch.Any(tup => (tup.one == prev.Polyomino && tup.two == generatedPolys[i].Polyomino) || (tup.two == prev.Polyomino && tup.one == generatedPolys[i].Polyomino)));
                if (already != -1)
                    availableColors = new List<int> { colors[already] };
                for (var j = 0; j < i; j++)
                    if (generatedPolys[j].Touches(generatedPolys[i]))
                        availableColors.Remove(colors[j]);
                if (availableColors.Count == 0)
                    goto tryAgain;
                colors[i] = availableColors[rnd.Next(0, availableColors.Count)];
            }

            var polyColors = Enumerable.Range(0, generatedPolys.Length).Select(ix => (poly: generatedPolys[ix], color: colors[ix])).ToArray();

            var colorIterations = 0;
            retryColors:
            colorIterations++;
            if (colorIterations > 10)
                goto tryAgain;

            polyColors.Shuffle(rnd);

            var keyPoly = polyColors.First(pc => pc.poly.Polyomino.Cells.Any(c => pc.poly.Place.AddWrap(c).Index == 0));
            var firstKeyColorIx = (polyColors.IndexOf(keyPoly) + polyColors.Length - 1) % polyColors.Length;

            var eqPolyIx = keyPoly.poly.Polyomino.Cells.IndexOf(c => keyPoly.poly.Place.AddWrap(c).Index == 0);
            var suitsTargetPermutation = Enumerable.Range(0, 4).ToArray().Shuffle(rnd);
            while (suitsTargetPermutation.IndexOf(3) == eqPolyIx)
                suitsTargetPermutation.Shuffle(rnd);
            var suitPartialPermutationColor = new[] { "012", "021", "102", "120", "201", "210" }.IndexOf(suitsTargetPermutation.Where(suit => suit != 3).JoinString());

            var eqDiamondsIx = suitsTargetPermutation.IndexOf(3);

            var colorStageColors = new List<int>
            {
                polyColors[firstKeyColorIx].color,
                polyColors[(firstKeyColorIx + 1) % polyColors.Length].color,
                polyColors[(firstKeyColorIx + 2) % polyColors.Length].color,
                suitPartialPermutationColor
            };
            var numColorsAv = Enumerable.Range(6, 4).Where(n => n != eqDiamondsIx + 1 && n != eqPolyIx + 1).ToArray();
            var numColors = numColorsAv[rnd.Next(0, numColorsAv.Length)];
            while (colorStageColors.Count < numColors)
            {
                var colorsAv = Enumerable.Range(0, 6).ToList();
                if (colorStageColors[colorStageColors.Count - 2] == colorStageColors[0] && colorStageColors[colorStageColors.Count - 1] == colorStageColors[1])
                    colorsAv.Remove(colorStageColors[2]);
                colorStageColors.Add(colorsAv[rnd.Next(0, colorsAv.Count)]);
            }

            for (var i = 1; i < colorStageColors.Count; i++)
                for (var j = 0; j < polyColors.Length; j++)
                    if (colorStageColors[i] == polyColors[j].color &&
                        colorStageColors[(i + 1) % colorStageColors.Count] == polyColors[(j + 1) % polyColors.Length].color &&
                        colorStageColors[(i + 2) % colorStageColors.Count] == polyColors[(j + 2) % polyColors.Length].color)
                        goto retryColors;

            var eqExtraCandidates = Enumerable.Range(1, 9).Except(new[] { colorStageColors.Count, eqDiamondsIx + 1, eqPolyIx + 1 }).ToArray();

            return new BlueButtonPuzzle
            {
                PolyominoSequence = polyColors.Select(tup => tup.poly).ToArray(),
                PolyominoSequenceColors = polyColors.Select(tup => tup.color).ToArray(),
                PolyominoGrid = generatedGrid,
                ColorStageColors = colorStageColors.ToArray(),
                EquationOffsets = new[] { colorStageColors.Count, eqDiamondsIx + 1, eqPolyIx + 1, eqExtraCandidates[rnd.Next(0, eqExtraCandidates.Length)] },
                Suits = suitsTargetPermutation,
                Jumps = Enumerable.Range(0, 4).Select(pos => (generatedJumps >> (2 * pos)) & 0x03).ToArray(),
                Word = word
            };
        }

        private static IEnumerable<(int[] solution, PolyominoPlacement[] polys)> SolvePolyominoPuzzle(
            int?[] sofar,
            int pieceIx,
            List<PolyominoPlacement> possiblePlacements,
            IEnumerable<(Polyomino one, Polyomino two)> notAllowedToTouch = null,
            List<PolyominoPlacement> polysSofar = null)
        {
            polysSofar ??= new List<PolyominoPlacement>();
            Coord? bestCell = null;
            int[] bestPlacementIxs = null;

            foreach (var tCell in Coord.Cells(GridWidth, GridHeight))
            {
                if (sofar[tCell.Index] != null)
                    continue;
                var tPossiblePlacementIxs = possiblePlacements.SelectIndexWhere(pl => pl.Polyomino.Has((tCell.X - pl.Place.X + GridWidth) % GridWidth, (tCell.Y - pl.Place.Y + GridHeight) % GridHeight)).ToArray();
                if (tPossiblePlacementIxs.Length == 0)
                    yield break;
                if (bestPlacementIxs == null || tPossiblePlacementIxs.Length < bestPlacementIxs.Length)
                {
                    bestCell = tCell;
                    bestPlacementIxs = tPossiblePlacementIxs;
                }
                if (tPossiblePlacementIxs.Length == 1)
                    goto shortcut;
            }

            if (bestPlacementIxs == null)
            {
                yield return (sofar.Select(i => i.Value).ToArray(), polysSofar.ToArray());
                yield break;
            }

            shortcut:
            var cell = bestCell.Value;

            foreach (var placementIx in bestPlacementIxs.Reverse())
            {
                var placement = possiblePlacements[placementIx];
                var (poly, place) = placement;
                possiblePlacements.RemoveAt(placementIx);

                foreach (var c in poly.Cells)
                    sofar[place.AddWrap(c).Index] = pieceIx;
                polysSofar.Add(new PolyominoPlacement(poly, place));

                var newPlacements = possiblePlacements
                    .Where(p => p.Polyomino != poly && p.Polyomino.Cells.All(c => sofar[p.Place.AddWrap(c).Index] == null))
                    .ToList();
                if (notAllowedToTouch != null)
                {
                    foreach (var (one, two) in notAllowedToTouch)
                        if (one == poly)
                            newPlacements.RemoveAll(pl => pl.Polyomino == two && pl.Touches(placement));
                        else if (two == poly)
                            newPlacements.RemoveAll(pl => pl.Polyomino == one && pl.Touches(placement));
                }

                foreach (var solution in SolvePolyominoPuzzle(sofar, pieceIx + 1, newPlacements, notAllowedToTouch, polysSofar))
                    yield return solution;

                polysSofar.RemoveAt(polysSofar.Count - 1);
                foreach (var c in poly.Cells)
                    sofar[place.AddWrap(c).Index] = null;
            }
        }

        private static Polyomino[] GetAllPolyominoes()
        {
            var basePolyominoes = Ut.NewArray(
                // domino
                "##",

                // triominoes
                "###",
                "##,#",

                // tetrominoes
                "####",     // I
                "##,##",    // O
                "###,#",    // L
                "##,.##",   // S
                "###,.#",   // T

                // pentominoes
                ".##,##,.#",    // F
                "#####",        // I
                "####,#",       // L
                "##,.###",      // N
                "##,###",       // P
                "###,.#,.#",    // T
                "###,#.#",      // U
                "###,#,#",      // V
                ".##,##,#",     // W
                ".#,###,.#",    // X
                "####,.#",      // Y
                "##,.#,.##"     // Z
            );

            return basePolyominoes
                .Select(p => new Polyomino(p))
                .SelectMany(p => new[] { p, p.RotateClockwise(), p.RotateClockwise().RotateClockwise(), p.RotateClockwise().RotateClockwise().RotateClockwise() })
                .SelectMany(p => new[] { p, p.Reflect() })
                .Distinct()
                .Where(poly => !poly.Cells.Any(c => c.X >= GridWidth || c.Y >= GridHeight)
                    && !poly.Cells.Any(c =>
                        (!poly.Has(c.X + 1, c.Y) && poly.Has((c.X + 1) % GridWidth, c.Y)) ||
                        (!poly.Has(c.X - 1, c.Y) && poly.Has((c.X + GridWidth - 1) % GridWidth, c.Y)) ||
                        (!poly.Has(c.X, c.Y + 1) && poly.Has(c.X, (c.Y + 1) % GridHeight)) ||
                        (!poly.Has(c.X, c.Y - 1) && poly.Has(c.X, (c.Y + GridHeight - 1) % GridHeight))))
                .ToArray();
        }

        private static List<PolyominoPlacement> GetAllPolyominoPlacements() =>
            (from poly in GetAllPolyominoes() from place in Enumerable.Range(0, GridWidth * GridHeight) select new PolyominoPlacement(poly, new Coord(GridWidth, GridHeight, place))).ToList();

        private static readonly Dictionary<char, int> _polyominoAlphabet = new Dictionary<char, int>
        {
            ['E'] = 0b11010,
            ['S'] = 0b11011,
            ['A'] = 0b11110,
            ['R'] = 0b01110,
            ['O'] = 0b01011,
            ['T'] = 0b01101,
            ['L'] = 0b00111,
            ['I'] = 0b11101,
            ['N'] = 0b10111,
            ['D'] = 0b10110,
            ['C'] = 0b11100,
            ['U'] = 0b10011,
            ['H'] = 0b11001,
            ['P'] = 0b01010,
            ['M'] = 0b10101,
            ['G'] = 0b01001,
            ['Y'] = 0b00110,
            ['B'] = 0b00011,
            ['F'] = 0b01100,
            ['W'] = 0b11000,
            ['K'] = 0b10010,
            ['V'] = 0b00101,
            ['X'] = 0b10001,
            ['Z'] = 0b10100,
            ['J'] = 0b00010,
            ['Q'] = 0b01000
        };

        private static (int[] solution, PolyominoPlacement[] polys, int jumps)? GenerateRandomPolyominoSolution(Random rnd, string word)
        {
            var allPlacements = GetAllPolyominoPlacements().Shuffle(rnd);
            var allJumps = Enumerable.Range(0, 256).ToArray().Shuffle(rnd);     // 4⁴ = 256
            foreach (var jumps in allJumps)
            {
                var letterPositions = Enumerable.Range(0, word.Length).Select(i => new Coord(GridWidth, GridHeight, i).AddYWrap((jumps >> (2 * i)) & 0x03)).ToArray();
                var placements = allPlacements.Where(polyPlacement => letterPositions.All((cell, ix) =>
                {
                    var (poly, place) = polyPlacement;
                    var dx = (cell.X - place.X + GridWidth) % GridWidth;
                    var dy = (cell.Y - place.Y + GridHeight) % GridHeight;
                    if (!poly.Has(dx, dy))
                        return true;
                    var encoding = _polyominoAlphabet[word[ix]];
                    return
                        poly.Has(dx, dy - 1) != ((encoding & 8) != 0) &&
                        poly.Has(dx + 1, dy) != ((encoding & 4) != 0) &&
                        poly.Has(dx, dy + 1) != ((encoding & 2) != 0) &&
                        poly.Has(dx - 1, dy) != ((encoding & 1) != 0) &&
                        (poly.Cells.Count() == 5) == ((encoding & 16) != 0);
                })).ToList();

                var solutionTup = SolvePolyominoPuzzle(new int?[GridWidth * GridHeight], 0, placements).FirstOrNull();
                if (solutionTup != null)
                    return (solutionTup.Value.solution, solutionTup.Value.polys, jumps);
            }
            return null;
        }
    }
}
