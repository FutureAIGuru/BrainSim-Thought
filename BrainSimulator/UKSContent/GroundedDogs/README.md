# Grounded Dogs demo

Load `DemoDogs.xml`. The Mental Model begins with Attention at its center and
shows its location as `Horiz` and `Vert` in the status area. Clicking another
Mental Model cell moves Attention there.

The Visual Input dropdown lists the files in `Observations`. Listing or
selecting a file does not import it. Enter the dog's distance and press
**Present**. Visual Input then:

1. imports the selected observation, creating that dog in the UKS;
2. removes the previous dog from the Mental Model but leaves it in the UKS;
3. places the selected dog at the current Attention location; and
4. sizes the displayed dog according to its distance.

The lighter Mental Model background marks the fixed visual field: `Horiz +/-60`
degrees and `Vert +/-40` degrees. A dog's 10-second Mental Model binding is
continually renewed while its cell remains in that field because it can still
be seen. Outside the field, renewal stops and the binding expires 10 seconds
after it was last visible. The dog and everything learned about it remain in
the UKS; only its current visual presence expires.

Visual Input gives a newly encountered subject the parent `Object`. An existing
subject keeps its current parents, so presenting a dog again after classing does
not restore a redundant direct `Object` parent.

Present Fido, Rover, and Spot individually. After all three have been observed,
use the unified **Agents** dialog to run **Class Create** and then **Attribute
Bubble**. These operations remain manual so the UKS can be inspected before and
after each step.

`hasImage` is marked with the `isGrounding` Property and is excluded from
classing and bubbling. The image labels are literal filenames resolved only
beneath this demo's `Images` directory.
