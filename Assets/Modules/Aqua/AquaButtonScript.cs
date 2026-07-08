using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BunchOfButtonsLib;
using KModkit;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class AquaButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMSelectable ButtonSelectable;
    public GameObject ButtonCap;

    public Transform FruitsParent;
    public Transform WordsParent;
    public Transform ResetParent;

    // Objects for instantiating/animating
    public MaskShaderManager MaskShaderManager;
    public MeshRenderer Mask;
    public TextMesh[] WordTexts;
    public TextMesh WordResultText;
    public TextMesh ResetText;
    public Mesh[] FruitMeshes;
    public Color[] FruitColors;
    public Light FruitsSpotlight;

    // Solving process
    private AquaButtonPuzzle _puzzle;
    private Stage _stage;
    private int _fruitHighlight;
    private int _wordHighlight;
    private int _wordSection;
    private int _wordProgress;
    private int _submissionIx;

    // Internals
    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Coroutine _pressHandler;
    private MaskMaterials _maskMaterials;

    public class FruitInfo
    {
        public FruitModel FruitModel;
        public FruitColor FruitColor;

        public FruitInfo(FruitModel fruitModel, FruitColor fruitColor)
        {
            FruitModel = fruitModel;
            FruitColor = fruitColor;
        }

        public bool Equals(FruitInfo other)
        {
            return other != null && other.FruitModel == FruitModel && other.FruitColor == FruitColor;
        }
    }

    public enum FruitModel
    {
        Apple,
        Banana,
        Cherry,
        Grape,
        Lemon,
        Pineapple,
        Strawberry
    }

    public enum FruitColor
    {
        Red,
        Yellow,
        Blue
    }

    public static readonly Dictionary<string, FruitInfo> _fruitInfoDict = new Dictionary<string, FruitInfo>
    {
        ["0"] = new FruitInfo(FruitModel.Grape, FruitColor.Yellow),
        ["1"] = new FruitInfo(FruitModel.Strawberry, FruitColor.Blue),
        ["2"] = new FruitInfo(FruitModel.Grape, FruitColor.Blue),
        ["3"] = new FruitInfo(FruitModel.Pineapple, FruitColor.Red),
        ["4"] = new FruitInfo(FruitModel.Apple, FruitColor.Yellow),
        ["5"] = new FruitInfo(FruitModel.Strawberry, FruitColor.Yellow),
        ["6"] = new FruitInfo(FruitModel.Grape, FruitColor.Red),
        ["1 1"] = new FruitInfo(FruitModel.Strawberry, FruitColor.Red),
        ["1 2"] = new FruitInfo(FruitModel.Banana, FruitColor.Blue),
        ["1 3"] = new FruitInfo(FruitModel.Lemon, FruitColor.Blue),
        ["1 4"] = new FruitInfo(FruitModel.Apple, FruitColor.Blue),
        ["2 1"] = new FruitInfo(FruitModel.Lemon, FruitColor.Red),
        ["2 2"] = new FruitInfo(FruitModel.Cherry, FruitColor.Red),
        ["2 3"] = new FruitInfo(FruitModel.Banana, FruitColor.Red),
        ["3 1"] = new FruitInfo(FruitModel.Pineapple, FruitColor.Yellow),
        ["3 2"] = new FruitInfo(FruitModel.Apple, FruitColor.Red),
        ["4 1"] = new FruitInfo(FruitModel.Banana, FruitColor.Yellow),
        ["1 1 1"] = new FruitInfo(FruitModel.Lemon, FruitColor.Yellow),
        ["1 1 2"] = new FruitInfo(FruitModel.Pineapple, FruitColor.Blue),
        ["1 2 1"] = new FruitInfo(FruitModel.Cherry, FruitColor.Blue),
        ["2 1 1"] = new FruitInfo(FruitModel.Cherry, FruitColor.Yellow)
    };

    enum Stage
    {
        Fruits,
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

        GeneratePuzzle();
        _stage = Stage.Fruits;

        StartCoroutine(AnimationManager(Stage.Fruits, FruitsParent, AnimateFruits, FruitsSpotlight));
        StartCoroutine(AnimationManager(new[] { Stage.Word, Stage.Solved }, WordsParent, AnimateWordsAndSolve));
        StartCoroutine(AnimationManager(Stage.Reset, ResetParent, AnimateReset));
    }

    private void GeneratePuzzle()
    {
        var seed = Rnd.Range(0, int.MaxValue);
        Debug.LogFormat("<The Aqua Button #{0}> Seed: {1}", _moduleId, seed);

        _puzzle = AquaButtonPuzzle.GeneratePuzzle(seed);

        Debug.Log($"[The Aqua Button #{_moduleId}] Clues are ordered from the bottom row going up to the left column going right.");
        Debug.Log($"[The Aqua Button #{_moduleId}] The given fruits are: {Enumerable.Range(0, 12).Select(x => $"{_fruitInfoDict[_puzzle.Clues[x]].FruitColor} {_fruitInfoDict[_puzzle.Clues[x]].FruitModel}").Join(", ")}.");
        Debug.Log($"[The Aqua Button #{_moduleId}] The nonogram clues represented by these fruits are: {Enumerable.Range(0, 12).Select(x => _puzzle.Clues[x]).Join(", ")}.");
        _submissionIx = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(BombInfo.GetSerialNumber()[0]) / 3;
        Debug.Log($"[The Aqua Button #{_moduleId}] The fruit to submit is the {_fruitInfoDict[_puzzle.Clues[_submissionIx]].FruitColor} {_fruitInfoDict[_puzzle.Clues[_submissionIx]].FruitModel} at {(_submissionIx < 6 ? "row " + (6 - _submissionIx) : "column " + ((_submissionIx % 6) + 1))}.");
        Debug.Log($"[The Aqua Button #{_moduleId}] The solution to the nonogram puzzle is:");
        for (int row = 0; row < 6; row++)
        {
            var bitmapRow = Enumerable.Range(row * 6, 6).Select(i => _puzzle.Bitmap[i] ? "█" : "░").Join("");
            Debug.Log($"[The Aqua Button #{_moduleId}] {bitmapRow}");
        }
        Debug.Log($"[The Aqua Button #{_moduleId}] The solution word is {_puzzle.Word}.");
    }

    private bool ButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
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
            case Stage.Fruits:
                var submittedFruit = $"{_fruitInfoDict[_puzzle.Clues[_fruitHighlight]].FruitColor} {_fruitInfoDict[_puzzle.Clues[_fruitHighlight]].FruitModel}";
                var correctFruit = $"{_fruitInfoDict[_puzzle.Clues[_submissionIx]].FruitColor} {_fruitInfoDict[_puzzle.Clues[_submissionIx]].FruitModel}";
                if (_fruitHighlight != _submissionIx)
                {
                    Debug.Log($"[The Aqua Button #{_moduleId}] Stage 1: You submitted the {submittedFruit} at {(_fruitHighlight < 6 ? "row " + (6 - _fruitHighlight) : "column " + ((_fruitHighlight % 6) + 1))} instead of the {correctFruit} at {(_submissionIx < 6 ? "row " + (6 - _submissionIx) : "column " + ((_submissionIx % 6) + 1))}. Strike.");
                    Module.HandleStrike();
                }
                else
                    _stage = Stage.Word;
                break;
            case Stage.Word:
                if (_wordSection == 0)
                {
                    if (_wordHighlight != (_puzzle.Word[_wordProgress] - 'A') / 9)
                    {
                        Debug.Log($"[The Aqua Button #{_moduleId}] Stage 2: You selected section {WordTexts[_wordHighlight].text} for letter #{_wordProgress + 1}. Strike!");
                        Module.HandleStrike();
                    }
                    else
                        _wordSection = _wordHighlight + 1;
                }
                else if (_wordSection <= 3)
                {
                    if (_wordHighlight != ((_puzzle.Word[_wordProgress] - 'A') % 9) / 3)
                    {
                        Debug.Log($"[The Aqua Button #{_moduleId}] Stage 2: You selected section {WordTexts[_wordHighlight].text} for letter #{_wordProgress + 1}. Strike!");
                        Module.HandleStrike();
                        _wordSection = 0;
                    }
                    else
                        _wordSection = (_wordSection - 1) * 3 + _wordHighlight + 4;
                }
                else if (_wordSection + _wordHighlight >= 30)
                {
                    Debug.Log($"[The Aqua Button #{_moduleId}] Stage 2: You submitted the empty slot after Z. Strike!");
                    Module.HandleStrike();
                    _wordSection = 0;
                }
                else
                {
                    var nextLetter = (char)('A' + ((_wordSection - 4) * 3 + _wordHighlight));
                    if (nextLetter != _puzzle.Word[_wordProgress])
                    {
                        Debug.Log($"[The Aqua Button #{_moduleId}] Stage 2: You submitted {nextLetter} for letter #{_wordProgress + 1}. Strike!");
                        Module.HandleStrike();
                    }
                    else
                    {
                        _wordProgress++;
                        if (_wordProgress == _puzzle.Word.Length)
                        {
                            Module.HandlePass();
                            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                            _stage = Stage.Solved;
                        }
                    }
                    _wordSection = 0;
                }
                break;

            case Stage.Reset:
                _stage = Stage.Fruits;
                break;
        }
    }

    private IEnumerator HandlePress()
    {
        yield return new WaitForSeconds(.5f);
        Audio.PlaySoundAtTransform("BlueButtonSwoosh", transform);
        _stage = Stage.Reset;
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

    private IEnumerator AnimationManager(Stage requiredStage, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        return AnimationManager(new[] { requiredStage }, parent, gen, spotlight);
    }

    private IEnumerator AnimationManager(Stage[] requiredStages, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        while (true)
        {
            parent.gameObject.SetActive(false);
            spotlight?.gameObject.SetActive(false);
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

    private GameObject MakeGameObject(string name, Transform parent, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        var obj = new GameObject(name);
        obj.transform.parent = parent;
        obj.transform.localPosition = position ?? new Vector3(0, 0, 0);
        obj.transform.localRotation = rotation ?? Quaternion.identity;
        obj.transform.localScale = scale ?? new Vector3(1, 1, 1);
        return obj;
    }

    private IEnumerator AnimateFruits(Func<bool> stop)
    {
        var scroller = MakeGameObject("Fruits scroller", FruitsParent);
        var width = 0f;
        var numCopies = 0;
        const float separation = .1f;
        const float spotlightDistance = 1f / 208 * 190;
        int randomOffset = Rnd.Range(0, 12);

        var axesRotators = NewArray(GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator(), GetRandomAxisRotator());
        var shapeObjs = new List<Transform>();

        while (width < .6f || numCopies < 2)
        {
            for (int i = 0; i < 12; i++)
            {
                int ix = (i + randomOffset) % 12;
                var shapeObj = MakeGameObject(string.Format("Shape {0}", ix + 1), scroller.transform, position: new Vector3(width, .01625f, 0), scale: new Vector3(.04f, .04f, .04f));
                shapeObj.AddComponent<MeshFilter>().sharedMesh = FruitMeshes[(int)_fruitInfoDict[_puzzle.Clues[ix]].FruitModel];
                var mr = shapeObj.AddComponent<MeshRenderer>();
                mr.material = _maskMaterials.DiffuseTint;
                mr.material.color = FruitColors[(int)_fruitInfoDict[_puzzle.Clues[ix]].FruitColor];

                width += separation;
                shapeObjs.Add(shapeObj.transform);
            }
            numCopies++;
        }
        width /= numCopies;
        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((.08f * Time.time) % width) - .15f, -.025f, 0);

            var pos = (((.08f * Time.time) % width) + .15f) / separation;
            var selected = Mathf.RoundToInt(pos);

            // Generated from Maple code; see Blue Button
            var t = pos - selected;
            const float r = -.3f, C1 = -3017.612937f, C2 = 1928.966946f, a = 6198.259105f, q = -.3990297758f, C4 = -525.3291758f, C5 = 461.5871550f;
            var calcAngle =
                t < q ? -.5f * a * Mathf.Pow(t, 2) + C1 * t + C4 :      // = d1(t)
                t < r ? .5f * a * Mathf.Pow(t, 2) + C2 * t + C5 :       // = d2(t)
                180 + Mathf.Atan2(t, spotlightDistance) * 180 / Mathf.PI;   // = d3(t)

            FruitsSpotlight.transform.localEulerAngles = new Vector3(40, calcAngle, 0);
            _fruitHighlight = (selected + randomOffset) % 12;
            var axisAngle = (90f * Time.time) % 360;
            var angle = (120f * Time.time) % 360;

            for (var i = 0; i < shapeObjs.Count; i++)
                shapeObjs[i].localRotation = Quaternion.AngleAxis(angle, axesRotators[i % 12](axisAngle) * Vector3.up);

            yield return null;
        }
        Destroy(scroller);
    }

    private T[] NewArray<T>(params T[] array) { return array; }

    private Func<float, Quaternion> GetRandomAxisRotator()
    {
        var rv1 = Rnd.Range(0f, 360f);
        var rv2 = Rnd.Range(0f, 360f);
        switch (Rnd.Range(0, 3))
        {
            case 0: return v => Quaternion.Euler(v, rv1, rv2);
            case 1: return v => Quaternion.Euler(rv1, v, rv2);
            default: return v => Quaternion.Euler(rv1, rv2, v);
        }
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
                WordResultText.text = _puzzle.Word;
                for (var i = 0; i < 3; i++)
                    WordTexts[i].gameObject.SetActive(false);
                yield return Animation(2.6f, t =>
                {
                    WordResultText.transform.localPosition = Vector3.Lerp(new Vector3(0, -.02f, -.03f), new Vector3(0, 0, 0), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.transform.localScale = Vector3.Lerp(new Vector3(.015f, .015f, .015f), new Vector3(.0225f, .0225f, .0225f), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.color = Color.Lerp(new Color32(0xE1, 0xE1, 0xE1, 0xFF), new Color32(0x0D, 0xE1, 0x0F, 0xFF), Easing.InOutQuad(t, 0, 1, 1));
                });
                yield break;
            }
            else
            {
                _wordHighlight = (int)((Time.time % 1.8f) / 1.8f * 3);
                for (var i = 0; i < 3; i++)
                {
                    WordTexts[i].gameObject.SetActive(true);
                    WordTexts[i].color = i == _wordHighlight ? Color.white : (Color)new Color32(0x1B, 0x37, 0x73, 0xFF);
                }
                WordResultText.text = _puzzle.Word.Substring(0, _wordProgress) + "_";

                if (_wordSection == 0)
                {
                    WordTexts[0].text = "A-I";
                    WordTexts[1].text = "J-R";
                    WordTexts[2].text = "S-Z";
                }
                else if (_wordSection <= 3)
                {
                    for (var triplet = 0; triplet < 3; triplet++)
                        WordTexts[triplet].text = _wordSection == 3 && triplet == 2 ? "YZ" : Enumerable.Range(0, 3).Select(ltr => (char)('A' + (_wordSection - 1) * 9 + 3 * triplet + ltr)).Join("");
                }
                else
                {
                    for (var ltr = 0; ltr < 3; ltr++)
                        WordTexts[ltr].text = _wordSection == 12 && ltr == 2 ? "" : ((char)('A' + ((_wordSection - 4) * 3 + ltr))).ToString();
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

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = " !{0} tap red apple/ra, yellow banana/yb [stage 1: wait for the specified sequence of fruits and press the last one specified; colors are r/y/b; shapes are a/b/c/g/l/p/s] | !{0} tap 1 3 2 3 1 [stage 2: tap when the highlight is in these positions] | !{0} reset";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        Match m;
        if (Regex.IsMatch(command, @"^\s*reset\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.75f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }
        if (_stage == Stage.Fruits && (m = Regex.Match(command, @"^\s*tap((?:[\s,;]+(?:[ryb]|red|yellow|blue)\s*(?:a(?:pple)?|b(?:anana)?|c(?:herry)?|g(?:rape)?|l(?:emon)?|p(?:ineapple)?|s(?:trawberry)?))+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var colors = new[] { "r", "y", "b" };
            var fruits = new[] { "a", "b", "c", "g", "l", "p", "s" };
            var pieces = m.Groups[1].Value.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var ixs = new List<FruitInfo>();
            for (var i = 0; i < pieces.Length; i++)
            {
                pieces[i] = pieces[i].ToLowerInvariant();
                int cIx, frIx;
                if (pieces[i].Length == 2 && (cIx = Array.IndexOf(colors, pieces[i].Substring(0, 1))) != -1 && (frIx = Array.IndexOf(fruits, pieces[i].Substring(1))) != -1)
                {
                    ixs.Add(new FruitInfo((FruitModel)frIx, (FruitColor)cIx));
                }
                else if ((cIx = Array.IndexOf(colors, pieces[i].Substring(0, 1))) != -1 && i < pieces.Length - 1 && (frIx = Array.IndexOf(fruits, pieces[i + 1].Substring(0, 2))) != -1)
                {
                    ixs.Add(new FruitInfo((FruitModel)frIx, (FruitColor)cIx));
                    i++;
                }
                else
                    yield break;
            }

            var ix = Enumerable.Range(0, _puzzle.Clues.Length)
                .IndexOf(startIx => Enumerable.Range(0, ixs.Count)
                .All(deepIx => _fruitInfoDict[_puzzle.Clues[(startIx + deepIx) % _puzzle.Clues.Length]].Equals(ixs[deepIx])));

            if (ix == -1)
            {
                yield return "sendtochaterror That sequence of fruits is not there.";
                yield break;
            }

            yield return null;
            while (_fruitHighlight != (ix + ixs.Count - 1) % _puzzle.Clues.Length)
                yield return null;
            ButtonSelectable.OnInteract();
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
        if (_stage == Stage.Fruits)
        {
            while (_fruitHighlight != _submissionIx)
                yield return true;
            ButtonSelectable.OnInteract();
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(1.5f);
        }

        while (_stage == Stage.Word)
        {
            var ltr = _puzzle.Word[_wordProgress] - 'A';
            var requiredHighlight =
                _wordSection == 0 ? ltr / 9 :
                _wordSection <= 3 ? (ltr / 3) % 3 : ltr % 3;

            while (_wordHighlight != requiredHighlight)
                yield return true;
            ButtonSelectable.OnInteract();
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.2f);
        }
    }
}
