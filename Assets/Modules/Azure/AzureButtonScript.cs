using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BunchOfButtonsLib;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class AzureButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMSelectable ButtonSelectable;
    public GameObject ButtonCap;

    public Transform CardsParent;
    public Transform NumbersParent;
    public Transform ArrowsParent;
    public Transform WordsParent;
    public Transform ResetParent;

    // Objects for instantiating/animating
    public MaskShaderManager MaskShaderManager;
    public MeshRenderer Mask;
    public Mesh[] Symbols;
    public Mesh[] Arrows;
    public TextMesh NumberTemplate;
    public TextMesh[] WordTexts;
    public TextMesh WordResultText;
    public TextMesh ResetText;
    public Color[] ShapeColors;
    public Light CardsSpotlight;

    // Solving process
    private AzureButtonPuzzle _puzzle;
    private Stage _stage;
    private int _numTaps;
    private Coroutine _numTapTimeout;
    private int _offset;
    private List<int> _cards;
    private List<int> _cardsShuffled;
    private int _shapeHighlight;
    private int _wordHighlight;
    private int _wordSection;
    private int _wordProgress;
    private bool _solved;

    // Internals
    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Coroutine _pressHandler;
    private MaskMaterials _maskMaterials;

    private static readonly string[] _colorNames = { "red", "yellow", "blue" };
    private static readonly string[] _shadings = { "solid", "striped", "outlined" };
    private static readonly string[] _shapeNames = { "capsule", "dumbbell", "diamond" };

    enum Stage
    {
        SETSymbols,
        Numbers,
        Arrows,
        Word,
        Reset,
        Solved
    }

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        ButtonSelectable.OnInteract += ButtonPress;
        ButtonSelectable.OnInteractEnded += ButtonRelease;

        _maskMaterials = MaskShaderManager.MakeMaterials();
        _maskMaterials.Text.mainTexture = WordResultText.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        _maskMaterials.DiffuseText.mainTexture = WordResultText.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Mask.sharedMaterial = _maskMaterials.Mask;

        Generate();
        _stage = Stage.SETSymbols;

        StartCoroutine(AnimationManager(Stage.SETSymbols, CardsParent, AnimateCards, CardsSpotlight));
        StartCoroutine(AnimationManager(Stage.Numbers, NumbersParent, AnimateNumbers));
        StartCoroutine(AnimationManager(Stage.Arrows, ArrowsParent, AnimateArrows));
        StartCoroutine(AnimationManager(new[] { Stage.Word, Stage.Solved }, WordsParent, AnimateWordsAndSolve));
        StartCoroutine(AnimationManager(Stage.Reset, ResetParent, AnimateReset));
    }

    private void Generate()
    {
        var seed = Rnd.Range(0, int.MaxValue);
        Debug.LogFormat("<The Azure Button #{0}> Seed: {1}", _moduleId, seed);
        _puzzle = AzureButtonPuzzle.Generate(seed);
        _offset = Rnd.Range(1, 10);
        _cards = _puzzle.SetS.Concat(_puzzle.SetE).Concat(new[] { _puzzle.CardT }).ToList();
        _cardsShuffled = _cards.ToList().Shuffle();
        if (Rnd.Range(0, 2) == 0 && !_cards.Any(x => x < _offset))
            _offset *= -1;

        Debug.LogFormat(@"[The Azure Button #{0}] Stage 1: Shuffled cards are: [ {1} ]", _moduleId, _cardsShuffled.Select(x => StringifyCard(x)).Join(", "));
        Debug.LogFormat(@"[The Azure Button #{0}] Stage 1: S.E.T.s are: [ {1} ] (S), [ {2} ] (E), {3} (T)", _moduleId, _puzzle.SetS.Select(x => StringifyCard(x)).Join(", "), _puzzle.SetE.Select(x => StringifyCard(x)).Join(", "), StringifyCard(_puzzle.CardT));

        Debug.LogFormat(@"[The Azure Button #{0}] Stage 2: Numbers shown: {1}", _moduleId, _cards.Take(6).Select(x => x + _offset).Join(", "));
        Debug.LogFormat(@"[The Azure Button #{0}] Stage 2: Card ternary digits are: [ {1} ] (S), [ {2} ] (E), {3} (T)", _moduleId,
            _puzzle.SetS.Select(x => ternary(x, 4)).Join(", "), _puzzle.SetE.Select(x => ternary(x, 4)).Join(", "), ternary(_puzzle.CardT, 4));
        Debug.LogFormat(@"[The Azure Button #{0}] Stage 2: Offset: {1}", _moduleId, _offset);
        Debug.LogFormat(@"[The Azure Button #{0}] Stage 2: Tap the button {1} time(s).", _moduleId, Math.Abs(_offset));

        var offsets = new[] { _puzzle.DecoyArrowPosition, 0, 1, 2, 3 };
        Func<int, string> getGridSvg = new Func<int, string>(gridIx =>
            string.Format("<g class='highlights'>{1}</g><path stroke-width='.1' d='M0 0h4v4h-4z' /><path stroke-width='.05' d='M1 0v4M2 0v4M3 0v4M0 1h4M0 2h4M0 3h4' /><g class='labels'>{0}</g>",
                Enumerable.Range(0, 16).Select(cell => string.Format("<text{3} x='{1}' y='{2}'>{0}</text>", _puzzle.Grid[cell], (cell % 4) + .5, (cell / 4) + .8, _puzzle.Arrows[gridIx].Coordinates.Any(c => c.AddWrap(offsets[gridIx], offsets[gridIx]).Index == cell) ? " class='highlighted'" : "")).Join(""),
                _puzzle.Arrows[gridIx].Coordinates.Select(c => c.AddWrap(offsets[gridIx], offsets[gridIx])).Select(c => string.Format("<rect x='{0}' y='{1}' width='1' height='1'/>", c.X, c.Y)).Join("")));

        var arrowsSvg = new StringBuilder();
        var dxs = new[] { 0, 1, 1, 1, 0, -1, -1, -1 };
        var dys = new[] { -1, -1, 0, 1, 1, 1, 0, -1 };
        for (var gridIx = 0; gridIx < _puzzle.Arrows.Length; gridIx++)
        {
            var arrow = _puzzle.Arrows[gridIx];
            var d1 = arrow.Directions[0];
            var path = string.Format("M {0} {1} {2} {3} l", Math.Cos((d1 + 6) * Math.PI / 4) * .4, Math.Sin((d1 + 6) * Math.PI / 4) * .4, dxs[d1], dys[d1]);
            var dots = new StringBuilder();
            for (var i = 1; i < arrow.Directions.Length; i++)
            {
                path += string.Format(" {0} {1}", dxs[arrow.Directions[i]], dys[arrow.Directions[i]]);
                dots.AppendFormat("<circle r='.1' cx='{0}' cy='{1}' />", arrow.Directions.Take(i).Sum(d => dxs[d]), arrow.Directions.Take(i).Sum(d => dys[d]));
            }
            arrowsSvg.AppendFormat(
                "<g fill='none' stroke-width='.05' transform='translate({0}, 0)'>" +
                    "{1}" +
                    "<g clip-path='url(#grid-clip-{4})'>" +
                        "<g class='arrow' transform='translate({2}, {2})'><circle r='.4' />{5}</g>" +
                    "</g>" +
                    "<g class='labels' font-size='.7' stroke='none'>{3}</g>" +
                "</g>",
                4.5 * gridIx,  // horizontal position
                getGridSvg(gridIx),    // grid
                offsets[gridIx] + .5,  // position of the arrow
                gridIx == 0 ? "<text x='2' y='4.8'>(decoy)</text>" : string.Format("<text x='2' y='4.8'>{0} = {1}</text>", ternary(_puzzle.SolutionWord[gridIx - 1] - 'A' + 1, 3), _puzzle.SolutionWord[gridIx - 1]), // bottom label
                _moduleId,  // part of clip ID

                // 9 copies of the arrow
                Enumerable.Range(0, 9).Select(ix => string.Format(
                    "<g transform='translate({5}, {6})'>" +
                        "<path d='{0}' />" +
                        "<path d='M-.15 .3 0 0 .15 .3' transform='translate({1}, {2}) rotate({3})'/>" +
                        "<g class='dots' stroke='none'>{4}</g>" +
                    "</g>",
                    path,   // arrow line
                    arrow.Directions.Sum(d => dxs[d]),  // arrow head X
                    arrow.Directions.Sum(d => dys[d]),  // arrow head Y
                    45 * arrow.Directions.Last(), // arrow head angle
                    dots,   // dots on the arrow
                    4 * (ix % 3 - 1), // transform X
                    4 * (ix / 3 - 1)  // transform Y
                )).Join("")
            );
        }

        Debug.LogFormat(@"[The Azure Button #{0}]=svg[Stage 3: Grid and arrows:]<svg class='azure-button' xmlns='http://www.w3.org/2000/svg' viewBox='-.2 -.2 22.4 5.4' text-anchor='middle' font-size='.9'><defs><clipPath id='grid-clip-{0}'><rect x='0' y='0' width='4' height='4' /></clipPath></defs>{1}</svg>",
            _moduleId, arrowsSvg);

        Debug.LogFormat(@"[The Azure Button #{0}] Stage 4: Answer is {1}", _moduleId, _puzzle.SolutionWord);
    }

    private static string ternary(int n, int numDigits)
    {
        var s = "";
        for (var i = 0; i < numDigits; i++)
        {
            s = (n % 3) + s;
            n /= 3;
        }
        return s;
    }

    private static string StringifyCard(int x)
    {
        return new[] { ((x / 3 % 3) + 1).ToString(), _colorNames[x / 9 % 3], _shadings[x % 3], _shapeNames[x / 27] + (x / 3 % 3 == 0 ? "" : "s") }.Join(" ");
    }

    private bool ButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (_numTapTimeout != null)
            StopCoroutine(_numTapTimeout);
        if (_stage != Stage.Solved)
            _pressHandler = StartCoroutine(HandlePress());
        return false;
    }

    private void ButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (_pressHandler != null)
            StopCoroutine(_pressHandler);

        switch (_stage)
        {
            case Stage.SETSymbols:
                if (_shapeHighlight != _cardsShuffled.IndexOf(_cards[6]))
                {
                    Debug.LogFormat(@"[The Azure Button #{0}] Stage 1: You submitted the {1}. Strike!", _moduleId, StringifyCard(_cardsShuffled[_shapeHighlight]));
                    Debug.LogFormat(@"<The Azure Button #{0}> Stage 1: You submitted card #{1}. Strike!", _moduleId, _shapeHighlight);
                    Module.HandleStrike();
                }
                else
                    _stage = Stage.Numbers;
                break;

            case Stage.Numbers:
                if (_numTapTimeout != null)
                    StopCoroutine(_numTapTimeout);
                _numTaps++;
                _numTapTimeout = StartCoroutine(NumberTimeout());
                break;

            case Stage.Arrows:
                _stage = Stage.Word;
                break;

            case Stage.Word:
                if (_wordSection == 0)
                {
                    if (_wordHighlight != (_puzzle.SolutionWord[_wordProgress] - 'A') / 9)
                    {
                        Debug.LogFormat(@"[The Azure Button #{0}] Stage 4: You selected section {1} for letter #{2}. Strike!", _moduleId, WordTexts[_wordHighlight].text, _wordProgress + 1);
                        Module.HandleStrike();
                    }
                    else
                        _wordSection = _wordHighlight + 1;
                }
                else if (_wordSection <= 3)
                {
                    if (_wordHighlight != ((_puzzle.SolutionWord[_wordProgress] - 'A') % 9) / 3)
                    {
                        Debug.LogFormat(@"[The Azure Button #{0}] Stage 4: You selected section {1} for letter #{2}. Strike!", _moduleId, WordTexts[_wordHighlight].text, _wordProgress + 1);
                        Module.HandleStrike();
                        _wordSection = 0;
                    }
                    else
                        _wordSection = (_wordSection - 1) * 3 + _wordHighlight + 4;
                }
                else if (_wordSection + _wordHighlight >= 30)
                {
                    Debug.LogFormat(@"[The Azure Button #{0}] Stage 4: You submitted the empty slot after Z. Strike!", _moduleId);
                    Module.HandleStrike();
                    _wordSection = 0;
                }
                else
                {
                    var nextLetter = (char) ('A' + ((_wordSection - 4) * 3 + _wordHighlight));
                    if (nextLetter != _puzzle.SolutionWord[_wordProgress])
                    {
                        Debug.LogFormat(@"[The Azure Button #{0}] Stage 4: You submitted {1} for letter #{2}. Strike!", _moduleId, nextLetter, _wordProgress + 1);
                        Module.HandleStrike();
                    }
                    else
                    {
                        _wordProgress++;
                        if (_wordProgress == _puzzle.SolutionWord.Length)
                        {
                            Module.HandlePass();
                            _solved = true;
                            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                            _stage = Stage.Solved;
                        }
                    }
                    _wordSection = 0;
                }
                break;

            case Stage.Reset:
                _stage = Stage.SETSymbols;
                break;
        }
    }

    private IEnumerator HandlePress()
    {
        yield return new WaitForSeconds(.5f);
        Audio.PlaySoundAtTransform("BlueButtonSwoosh", transform);
        _stage = Stage.Reset;
    }

    private IEnumerator AnimationManager(Stage requiredStage, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        return AnimationManager(new[] { requiredStage }, parent, gen, spotlight);
    }

    private IEnumerator AnimationManager(Stage[] requiredStages, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        while (true)
        {
            parent.gameObject.SetActive(false);
            if (spotlight != null)
                spotlight.gameObject.SetActive(false);

            while (!requiredStages.Contains(_stage))
            {
                if (_stage == Stage.Solved)
                    yield break;
                yield return null;
            }

            var stop = false;
            StartCoroutine(gen(() => stop));
            yield return new WaitForSeconds(1f);
            parent.localPosition = new Vector3(0, 0, -.2f);
            parent.gameObject.SetActive(true);
            if (spotlight != null)
            {
                spotlight.intensity = 0;
                spotlight.gameObject.SetActive(true);
            }
            yield return Animation(.63f, t =>
            {
                parent.localPosition = new Vector3(0, 0, Easing.BackOut(t, -.2f, 0, 1));
                if (spotlight != null)
                    spotlight.intensity = 5 * t;
            });

            while (requiredStages.Contains(_stage))
            {
                if (_stage == Stage.Solved)
                    yield break;
                yield return null;
            }

            yield return Animation(.63f, t =>
            {
                parent.localPosition = new Vector3(0, 0, Easing.BackIn(t, 0, .2f, 1));
                if (spotlight != null)
                    spotlight.intensity = 5 * (1 - t);
            });

            stop = true;
        }
    }

    private IEnumerator Animation(float duration, Action<float> action)
    {
        var elapsed = 0f;
        while (elapsed < duration)
        {
            action(elapsed / duration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        action(1);
    }

    private IEnumerator AnimateButton(float a, float b)
    {
        var duration = 0.1f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            ButtonCap.transform.localPosition = new Vector3(0f, Easing.InOutQuad(elapsed, a, b, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        ButtonCap.transform.localPosition = new Vector3(0f, b, 0f);
    }

    private GameObject MakeGameObject(string name, Transform parent, float scale, Vector3? position = null, Quaternion? rotation = null)
    {
        return MakeGameObject(name, parent, position, rotation, scale: new Vector3(scale, scale, scale));
    }
    private GameObject MakeGameObject(string name, Transform parent, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        var obj = new GameObject(name);
        obj.transform.parent = parent;
        obj.transform.localPosition = position ?? new Vector3(0, 0, 0);
        obj.transform.localRotation = rotation ?? Quaternion.identity;
        obj.transform.localScale = scale ?? new Vector3(1, 1, 1);
        return obj;
    }

    private IEnumerator AnimateCards(Func<bool> stop)
    {
        var scroller = MakeGameObject("Cards scroller", CardsParent);
        var width = 0f;
        var numCopies = 0;
        const float separation = .035f;
        const float spotlightDistance = 1f / 208 * 190;
        var symbols = new List<List<List<GameObject>>>();

        while (width < .6f || numCopies < 2)
        {
            var bunch = new List<List<GameObject>>();
            for (int i = 0; i < 7; i++)
            {
                var cardObjs = new List<GameObject>();
                var cardParent = MakeGameObject(string.Format("Card {0}", i + 1), scroller.transform, position: new Vector3(), scale: new Vector3(1f, 1f, 1f));
                for (int j = 0; j < (_cardsShuffled[i] / 3 % 3) + 1; j++)
                {
                    var shapeObj = MakeGameObject(string.Format("Symbol {0}", j + 1), cardParent.transform, position: new Vector3(width, 0, 0), scale: new Vector3(1.35f, 1.35f, 1.35f));
                    cardObjs.Add(shapeObj);
                    shapeObj.AddComponent<MeshFilter>().sharedMesh = Symbols[(_cardsShuffled[i] / 27) + (_cardsShuffled[i] % 3) * 3];
                    var mr = shapeObj.AddComponent<MeshRenderer>();
                    mr.material = _maskMaterials.DiffuseTint;
                    mr.material.color = ShapeColors[_cardsShuffled[i] / 9 % 3];
                    width += separation;
                }
                bunch.Add(cardObjs);
                width += separation;
            }
            numCopies++;
            symbols.Add(bunch);
        }
        width /= numCopies;

        var poses = new List<int>();
        var mids = new List<int>();
        var vw = 0;
        for (var i = 0; i < 7; i++)
        {
            var cardWidth = (_cardsShuffled[i] / 3 % 3) * 2;
            poses.Add(vw + cardWidth + 1);
            mids.Add(vw + cardWidth / 2);
            vw += cardWidth + 4;
        }

        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((.05f * Time.time) % width) - .15f, -.025f, 0);

            const float bobIntensity = 0.01f;
            const float bobSpeed = 3.5f;
            const float rotationSpeed = 90f;
            foreach (var bunch in symbols)
            {
                for (int symbolIx = 0; symbolIx < bunch.Count; symbolIx++)
                {
                    List<GameObject> card = bunch[symbolIx];
                    for (int cardIx = 0; cardIx < card.Count; cardIx++)
                    {
                        card[cardIx].transform.localEulerAngles = new Vector3(0, 0, Time.time * rotationSpeed + cardIx * 60);
                        card[cardIx].transform.localPosition = new Vector3(card[cardIx].transform.localPosition.x, 0.005f, Mathf.Sin(card[cardIx].transform.localPosition.x / width * 4 * Mathf.PI + Time.time * bobSpeed) * bobIntensity);
                    }
                }
            }

            var pos = ((((.05f * Time.time) % width) + .15f) / (separation / 2)) % vw;
            var selected = poses.IndexOf(p => p > pos);
            if (selected == -1)
            {
                selected = 0;
                pos -= vw;
            }
            var curSize = mids[selected] - (selected == 0 ? poses.Last() - vw : poses[selected - 1]) - 2;
            var prevSize = poses[(selected + poses.Count - 1) % poses.Count] - mids[(selected + mids.Count - 1) % mids.Count];

            /*
            Code to generate Maple code to calculate and generate the following coefficients:

            for (var prev = 1; prev <= 3; prev++)
                for (var next = 1; next <= 3; next++)
                {
                    Console.WriteLine($"{prev}, {next}");
                    Clipboard.SetText($@"restart;
minT := {(-2 - next) / 6d};
prevMaxT := {prev / 6d};
r := minT + .2;
v1 := t -> -a*t+C1;
v2 := t -> a*t+C2;
v3 := t -> 57.29577951/s*(1+t^2/s^2);
d1 := t -> -1/2*a*t^2 + C1*t + C4;
d2 := t -> 1/2*a*t^2 + C2*t + C5;
d3 := t -> arctan(t/s)*180/3.1415926535897932384626433832795+180;
s := 1.0 / 208 * 190;
solve({{
    v1(minT) = v3(prevMaxT),
    v1(q) = v2(q),
    v2(r) = v3(r),
    d1(minT) = d3(prevMaxT),
    d1(q) = d2(q),
    d2(r) = d3(r)
}}, {{ C1, C2, C4, C5, a, q }});");
                    Console.ReadLine();
                }

            */

            var t = (pos - mids[selected % mids.Count]) / 6;
            float r = (-2 - curSize) / 6f + .2f;

            float C2, a, C5, C1, C4, q;
            if (prevSize == 1 && curSize == 1) { a = 4195.290843f; q = -.4005574492f; C1 = -2032.833530f; C5 = 371.4535832f; C4 = -301.6651846f; C2 = 1328.076467f; }
            else if (prevSize == 1 && curSize == 2) { C2 = 2496.528317f; C5 = 753.9171885f; C1 = -3388.665089f; q = -.5680452329f; a = 5180.215470f; C4 = -917.6108407f; }
            else if (prevSize == 1 && curSize == 3) { C2 = 3948.368536f; a = 6087.620064f; q = -.7356383340f; C4 = -1869.406832f; C1 = -5008.204829f; C5 = 1424.992522f; }
            else if (prevSize == 2 && curSize == 1) { C1 = -2543.202539f; a = 5228.557404f; C4 = -417.9839471f; C5 = 417.9505785f; C2 = 1638.056436f; q = -.3998482422f; }
            else if (prevSize == 2 && curSize == 2) { C4 = -1133.207043f; C2 = 2978.435907f; C5 = 866.3622929f; C1 = -4070.840231f; a = 6212.874591f; q = -.5673119612f; }
            else if (prevSize == 2 && curSize == 3) { a = 7119.439346f; C5 = 1631.929611f; C1 = -5861.789959f; C2 = 4601.854081f; C4 = -2212.749768f; q = -.7348643293f; }
            else if (prevSize == 3 && curSize == 1) { C1 = -3017.612937f; a = 6198.259105f; C2 = 1928.966946f; C5 = 461.5871550f; C4 = -525.3291758f; q = -.3990297758f; }
            else if (prevSize == 3 && curSize == 2) { C5 = 971.8638654f; C2 = 3430.585503f; a = 7181.766584f; q = -.5664980323f; C1 = -4706.327773f; C4 = -1332.908815f; }
            else { a = 8087.299740f; q = -.7340356204f; C5 = 1826.039390f; C2 = 5214.832330f; C4 = -2531.464770f; C1 = -6657.899834f; }

            var calcAngle =
                t < q ? -.5f * a * Mathf.Pow(t, 2) + C1 * t + C4 :
                t < r ? .5f * a * Mathf.Pow(t, 2) + C2 * t + C5 :
                180 + Mathf.Atan2(t, spotlightDistance) * 180 / Mathf.PI;

            CardsSpotlight.transform.localEulerAngles = new Vector3(40, calcAngle, 0);
            _shapeHighlight = selected % 7;

            yield return null;
        }

        Destroy(scroller);
    }

    private IEnumerator AnimateNumbers(Func<bool> stop)
    {
        var scroller = MakeGameObject("Numbers scroller", NumbersParent, scale: .025f);
        var width = 0f;
        var numCopies = 0;

        while (width < 24 || numCopies < 2)
        {
            for (int i = 0; i < 6; i++)
            {
                var equation = Instantiate(NumberTemplate, scroller.transform);
                equation.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
                equation.name = string.Format("Number #{0}", i + 1);
                equation.transform.localPosition = new Vector3(width, 0, 0);
                equation.transform.localEulerAngles = new Vector3(90, 0, 0);
                equation.transform.localScale = new Vector3(1, 1, 1);
                equation.gameObject.SetActive(true);
                equation.text = (_cards[i] + _offset).ToString();
                width += 1.5f * equation.text.Length + 2f;
            }
            numCopies++;
        }
        width /= numCopies;

        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((6f * Time.time) % width) * .025f - 0.15f, -0.025f, 0);
            yield return null;
        }

        Destroy(scroller);
    }

    private IEnumerator AnimateArrows(Func<bool> stop)
    {
        const float scale = 1.3f;

        var scroller = MakeGameObject("Arrows scroller", ArrowsParent, scale: .025f);
        var width = 0f;
        var numCopies = 0;

        while (width < 24 || numCopies < 2)
        {
            for (int i = 0; i < _puzzle.Arrows.Length; i++)
            {
                width += _puzzle.Arrows[i].Width * scale / 2f;
                var arrowObj = MakeGameObject(string.Format("Arrow {0}", i + 1), scroller.transform,
                    position: new Vector3(width - _puzzle.Arrows[i].CenterX * scale, 0, _puzzle.Arrows[i].CenterY * scale),
                    rotation: Quaternion.Euler(0, _puzzle.Arrows[i].Rotation, 0),
                    scale: new Vector3(scale, scale, scale));
                arrowObj.AddComponent<MeshFilter>().sharedMesh = Arrows.Where(x => x.name == _puzzle.Arrows[i].ModelName).First();
                var mr = arrowObj.AddComponent<MeshRenderer>();
                mr.material = _maskMaterials.DiffuseTint;
                mr.material.color = new Color32(0x81, 0xc0, 0xff, 0xff);
                width += _puzzle.Arrows[i].Width * scale / 2f + 1f;
            }
            numCopies++;
        }
        width /= numCopies;

        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((3f * Time.time) % width) * .025f - 0.15f, -0.025f, 0);
            yield return null;
        }

        Destroy(scroller);
    }

    private IEnumerator AnimateWordsAndSolve(Func<bool> stop)
    {
        WordResultText.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
        for (var i = 0; i < 3; i++)
            WordTexts[i].GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;

        while (!stop())
        {
            if (_stage == Stage.Solved)
            {
                WordResultText.text = _puzzle.SolutionWord;

                for (var i = 0; i < 3; i++)
                    WordTexts[i].gameObject.SetActive(false);

                yield return Animation(2.6f, t =>
                {
                    WordResultText.transform.localPosition = Vector3.Lerp(new Vector3(0, -.02f, -.03f), new Vector3(0, 0, 0), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.transform.localScale = Vector3.Lerp(new Vector3(.015f, .015f, .015f), new Vector3(.025f, .025f, .025f), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.color = Color.Lerp(new Color32(0xE1, 0xE1, 0xE1, 0xFF), new Color32(0x0D, 0xE1, 0x0F, 0xFF), Easing.InOutQuad(t, 0, 1, 1));
                });
                yield break;
            }
            else
            {
                _wordHighlight = (int) ((Time.time % 1.8f) / 1.8f * 3);
                for (var i = 0; i < 3; i++)
                {
                    WordTexts[i].gameObject.SetActive(true);
                    WordTexts[i].color = i == _wordHighlight ? Color.white : (Color) new Color32(0x50, 0x7E, 0xAB, 0xFF);
                }
                WordResultText.text = _puzzle.SolutionWord.Substring(0, _wordProgress) + "_";

                if (_wordSection == 0)
                {
                    WordTexts[0].text = "A–I";
                    WordTexts[1].text = "J–R";
                    WordTexts[2].text = "S–Z";
                }
                else if (_wordSection <= 3)
                {
                    for (var triplet = 0; triplet < 3; triplet++)
                        WordTexts[triplet].text = _wordSection == 3 && triplet == 2 ? "YZ" : Enumerable.Range(0, 3).Select(ltr => (char) ('A' + (_wordSection - 1) * 9 + 3 * triplet + ltr)).Join("");
                }
                else
                {
                    for (var ltr = 0; ltr < 3; ltr++)
                        WordTexts[ltr].text = _wordSection == 12 && ltr == 2 ? "" : ((char) ('A' + ((_wordSection - 4) * 3 + ltr))).ToString();
                }

                yield return null;
            }
        }

        for (var i = 0; i < 3; i++)
            WordTexts[i].gameObject.SetActive(false);
    }

    private IEnumerator AnimateReset(Func<bool> stop)
    {
        ResetText.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
        ResetText.gameObject.SetActive(true);
        while (!stop())
            yield return null;
        ResetText.gameObject.SetActive(false);
    }

    private IEnumerator NumberTimeout()
    {
        yield return new WaitForSeconds(1.5f);
        if (_numTaps != Math.Abs(_offset))
        {
            Debug.LogFormat(@"[The Azure Button #{0}] Stage 2: You tapped the button {1} times instead of {2}. Strike!", _moduleId, _numTaps, Math.Abs(_offset));
            Module.HandleStrike();
        }
        else
            _stage = Stage.Arrows;
        _numTaps = 0;
    }


#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} tap 3 red striped capsules / 3rstca / 1 purple solid dumbbell / 1psodu [stage 1: tap when the specified card is highlighted; colors are r/y/b; shadings are so/st/ou; shapes are ca/du/di] | !{0} tap 5 [stage 2: tap 5 times] | !{0} tap [stage 3] | !{0} tap 1 3 2 3 1 [stage 4: tap when the highlight is in these positions] | !{0} reset";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        if (Regex.IsMatch(command, @"^\s*reset\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.75f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        Match m;
        if (_stage == Stage.SETSymbols && (m = Regex.Match(command, @"^\s*tap\s*
            (?<n>[1-3])\s*
            (?:(?<r>red|r)|(?<y>yellow|y)|blue|b)\s*
            (?:(?<so>so(?:lid)?)|(?<st>st(?:riped)?)|ou(?:tlined)?)\s*
            (?:(?<ca>ca(?:psules?)?)|(?<du>du(?:mbbells?)?)|di(?:amonds?)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)).Success)
        {
            var targetCard = 27 * (m.Groups["ca"].Success ? 0 : m.Groups["du"].Success ? 1 : 2) +
                9 * (m.Groups["r"].Success ? 0 : m.Groups["y"].Success ? 1 : 2) +
                3 * (int.Parse(m.Groups["n"].Value) - 1) +
                (m.Groups["so"].Success ? 0 : m.Groups["st"].Success ? 1 : 2);

            var ix = _cardsShuffled.IndexOf(targetCard);
            if (ix == -1)
            {
                yield return "sendtochaterror That card is not there.";
                yield break;
            }
            yield return null;
            while (_shapeHighlight != ix)
                yield return null;
            ButtonSelectable.OnInteract();
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        if (_stage == Stage.Numbers && (m = Regex.Match(command, @"^\s*tap\s+([1-9])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            int val;
            if (!int.TryParse(m.Groups[1].Value, out val))
            {
                yield return "sendtochaterror How many times should I tap it?";
                yield break;
            }
            yield return null;
            for (; val > 0; val--)
            {
                ButtonSelectable.OnInteract();
                yield return new WaitForSeconds(.1f);
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.1f);
            }
            yield break;
        }

        if (_stage == Stage.Arrows && Regex.IsMatch(command, @"^\s*tap\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        if (_stage == Stage.Word && (m = Regex.Match(command, @"^\s*tap\s+([,; 123]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            yield return null;
            foreach (var ch in m.Groups[1].Value)
            {
                if (ch < '1' || ch > '3')
                    continue;
                while (_wordHighlight != ch - '1')
                    yield return null;
                ButtonSelectable.OnInteract();
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.2f);
            }
            yield break;
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        while (!_solved)
        {
            if (_stage == Stage.SETSymbols)
            {
                var targetCard = _cardsShuffled.IndexOf(_cards[6]);
                while (_shapeHighlight != targetCard)
                    yield return true;
                ButtonSelectable.OnInteract();
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(1.5f);
            }

            if (_stage == Stage.Numbers)
            {
                while (_numTaps < Math.Abs(_offset))
                {
                    ButtonSelectable.OnInteract();
                    yield return new WaitForSeconds(.1f);
                    ButtonSelectable.OnInteractEnded();
                    yield return new WaitForSeconds(.1f);
                }
            }

            if (_stage == Stage.Arrows)
            {
                yield return new WaitForSeconds(1f);
                ButtonSelectable.OnInteract();
                yield return new WaitForSeconds(.1f);
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(1.5f);
            }

            while (_stage == Stage.Word)
            {
                var ltr = _puzzle.SolutionWord[_wordProgress] - 'A';
                var requiredHighlight =
                    _wordSection == 0 ? ltr / 9 :
                    _wordSection <= 3 ? (ltr / 3) % 3 : ltr % 3;

                while (_wordHighlight != requiredHighlight)
                    yield return true;
                ButtonSelectable.OnInteract();
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.2f);
            }
            yield return true;
        }
    }
}
