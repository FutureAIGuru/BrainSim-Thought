# Grounded Dogs demo

Load `DemoDogs.xml`. The Mental Model begins with Attention at its center and
shows its location as `Horiz` and `Vert` in the status area. Clicking another
Mental Model cell moves Attention there.

The Visual Input dropdown lists the anonymous files `O1.txt`, `O2.txt`, and
`O3.txt` in `Observations`. Listing the files does not import them. The files
contain visible attributes and an image filename, but contain no Fido, Rover,
or Spot name. Enter the dog's distance and select an observation; selection
presents it immediately. Visual Input then:

1. searches the descendants of `Object` for a unique attribute match;
2. reuses the match, or creates the next anonymous `O` number when no match exists;
3. adds only attributes which are not already available through inheritance;
4. leaves any existing scene contents in place;
5. places the selected object at the current Attention location; and
6. sizes the displayed object according to its distance.

Use **Clear** when the scene should be emptied. This makes it possible to build
a scene containing several objects before clearing it explicitly.

Distance 1 is the closest supported distance and displays the image at about
100 pixels tall. Greater distances reduce the image size.

The lighter Mental Model background marks the fixed visual field: `Horiz +/-56`
degrees and `Vert +/-30` degrees. A dog's 10-second Mental Model binding is
continually renewed while its cell remains in that field because it can still
be seen. Outside the field, renewal stops and the binding expires 10 seconds
after it was last visible. The dog and everything learned about it remain in
the UKS; only its current visual presence expires.

Visual Input gives a newly encountered subject the parent `Object`. An existing
subject keeps its current parents, so presenting it again after classing does
not restore a redundant direct `Object` parent or duplicate attributes which
have bubbled to its class.

Present O1, O2, and O3 individually. After all three have been observed,
use the unified **Agents** dialog to run **Class Create** and then **Attribute
Bubble**. These operations remain manual so the UKS can be inspected before and
after each step.

After classing and bubbling, select `Dogs-Language-1.txt` in Visual Input. Use
**Step** to present one visual or heard phrase at a time. **Run** advances the
same lesson on a non-blocking timer, rewinds automatically, and can be paused.
A `show` event presents an observation without clearing the scene, and a
`clear` event empties it. Locations and Attention can be controlled in a lesson:

```
attention horiz -20 vert 0
show O1.txt distance 1 horiz -20 vert 0
hear Good dog.
clear
```

The `horiz` and `vert` options may appear in either order. A coordinate omitted
from `show` uses the corresponding current Attention coordinate; with no
location options, the item appears at Attention. Add the standalone
`appearance` option to place another spatial appearance of the same recognized
Thought without moving its existing appearances. A `fill` command can cover a
region of the visible Mental Model, for example:

```
fill O7.txt distance 8 above 20
fill O8.txt distance 6 below -20
```

A `hear` event creates weak, plastic `means` relationships from each word to
the attended individual and its non-root ancestors. A meaning strengthens only
when both its word is heard and its target is attended. It decays when the word
occurs without that target, or when the target is attended without that word.
This allows shared words such as `dog` to converge on the shared dog class,
proper names to converge on individuals, and broadly reused words such as `is`
to lose unsupported candidate meanings. Structural targets including
`activeThought`, `imaginedThought`, `inActiveThought`, `attention`,
`mentalModel`, and `Abstract` are neither new meaning candidates nor selectable
word meanings; old plastic links to them decay. Once one meaning reaches 0.9,
alternatives for that word below 0.5 are removed. Both thresholds are mutable
module settings.

When an observation is presented again, the **Word** field displays the best
word which currently means the recognized anonymous object. Conversely, type a
known word in that field and press Enter or **Imagine** to place its strongest
meaning into the Mental Model at Attention. Thus the demonstration can show
both directions without assigning a proper-name label to the internal object:
seeing O1 can produce `fido`, and hearing `fido` can evoke O1. Imagine also
uses the current Distance selection, so the imagined marker is scaled exactly
like a perceived object at that distance.

Mental Model entries marked `imaginedThought` are drawn partially transparent
and labeled `(imagined)`. If an imagined meaning has no image, as with the
anonymous dog class, it is shown as a concept card containing its known direct
and inherited attributes, such as `[can->bark] [has->fur]`. Grounding links and
class-management metadata are omitted from the card. Imagined entries are not
renewed by the visual field, so they fade and expire on the same ten-second
timescale as scene entries which are no longer visible.

This small lesson provides only dog-centered examples, so `this` and `is` can
also converge on the anonymous dog class. The learner has no evidence here that
`dog` names the class while `this` is deictic and `is` is grammatical. This is
an intentional demonstration of underdetermination; broader experience with
other object classes and sentence forms is needed to separate those meanings.
If a lesson reaches `hear` while Attention is empty, that command is shown as
skipped and the lesson continues.

`Dogs-Language-1.txt` declares `language English`, while
`Chiens-Langue-1.txt` declares `language French`. Meaning competition is scoped
to that lesson language, so presenting the same objects during the French
lesson does not weaken English-only words such as `dog`. Words heard in both
languages, including Fido, Rover, and Spot, are associated with both language
contexts while continuing to use the same Word Thought and object meaning.

`Scene.txt` is a demonstration-only additive scene. Stepping through it places
three dogs and three trees, fills every visible cell above Vert +20 with
appearances of one sky Thought, fills the corresponding lower band with one
grass Thought, then returns Attention to the center. Farther entries are drawn
first, allowing nearer trees and dogs to paint over the background. The tree,
sky, and grass files are anonymous observations and contain no heard phrases,
so merely showing the scene does not teach their English labels.

In the UKS dialog, select `Word` as the root, enable **Weight Bars**, and expand
a word such as `w:dog` to watch its candidate `means` relationships change.
**Show Details** continues to display the exact numeric weight.

`hasImage` is marked with the `isGrounding` Property and is excluded from
classing and bubbling. The image labels are literal filenames resolved only
beneath a configured grounded-content `Images` directory. The original
`GroundedDogs` directory remains supported; additional observations, images,
and lessons can be placed beneath `GroundedExperiences`.
