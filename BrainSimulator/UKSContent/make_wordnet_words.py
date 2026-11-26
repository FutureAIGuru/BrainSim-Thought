"""Builds UKSContent/WordNetWords.txt from WordNet 3.1 (index.*, data.*, *.exc).
usage: python -I make_wordnet_words.py <wordnet dict dir> <BasicWords.txt> <output> [--all]
By default only lemmas that occur in WordNet's sense-tagged corpus (tagsense_cnt > 0): the full
list (85k entries) takes the UKS about 20 minutes to load."""
import re, sys
from collections import defaultdict

dict_dir, basic_words, out_path = sys.argv[1:4]
include_all = '--all' in sys.argv[4:]
POS = {'noun': 'n', 'verb': 'v', 'adj': 'a', 'adv': 'r'}
CLASS = {'v': 'verb', 'a': 'adjective', 'r': 'adverb'}
word_re = re.compile(r'^[a-z]+$')

# Spellings BasicWords already has (its words and forms win), with what it says about them: each
# word's classes, the class tree (BasicWords and Inflections.txt beside it), and which words are
# forms of which (a verb's stem is the stored plural of its third-person entry).
known = set()
labels_spelled = defaultdict(list)   # spelling -> BasicWords labels
word_classes = defaultdict(set)      # w: label -> its is-a targets
class_parents = defaultdict(set)     # class -> its is-a targets
form_of = {}                         # form label -> (base label, link type)
for path in [basic_words, re.sub(r'BasicWords\.txt$', 'Inflections.txt', basic_words)]:
    for line in open(path, encoding='utf-8'):
        m = re.search(r'\[(w:[^\]-]+)->spelled->\^([^\]]+)\]', line)
        if m:
            spelling = m.group(2).replace(' ', '').lower()
            known.add(spelling)
            labels_spelled[spelling].append(m.group(1))
        m = re.match(r'\[([^\[\]]+)->is-a->([^\[\]]+)\]', line)
        if m: (word_classes if m.group(1).startswith('w:') else class_parents)[m.group(1)].add(m.group(2))
        m = re.match(r'\[(w:[^\[\]]+)->(has[A-Za-z]+)->(w:[^\[\]]+)\]', line)
        if m: form_of[m.group(3)] = (m.group(1), m.group(2))

def has_ancestor(wclass, ancestor, seen=None):
    if wclass == ancestor: return True
    seen = seen or set()
    if wclass in seen: return False
    seen.add(wclass)
    return any(has_ancestor(parent, ancestor, seen) for parent in class_parents.get(wclass, ()))

def already_has(label, wclass):
    if any(has_ancestor(c, wclass) for c in word_classes[label] if c != 'wordForm'): return True
    # A verb's stem (w:chase, the plural of w:chases) stands for the verb itself.
    base = form_of.get(label)
    return wclass == 'verb' and base is not None and base[1] == 'hasPlural' and \
        any(has_ancestor(c, 'verb') for c in word_classes[base[0]])

# Lemmas per part of speech, and which nouns ever appear lowercase (else proper).
lemmas = {p: set() for p in POS.values()}
for name, p in POS.items():
    for line in open(f'{dict_dir}/index.{name}', encoding='utf-8'):
        if line.startswith('  '): continue
        fields = line.split()
        lemma, pointer_count = fields[0], int(fields[3])
        tagged = int(fields[4 + pointer_count + 1])
        if word_re.match(lemma) and len(lemma) > 1 and (include_all or tagged > 0): lemmas[p].add(lemma)
lowercase_noun = set()
for line in open(f'{dict_dir}/data.noun', encoding='utf-8'):
    if line.startswith('  '): continue
    fields = line.split(' ')
    count = int(fields[3], 16)
    for i in range(count):
        word = re.sub(r'\(.*\)$', '', fields[4 + 2 * i])
        if word.islower(): lowercase_noun.add(word)

def spelled(word): return '^' + ' '.join(word)
BASIC_START = "# Classes WordNet gives words BasicWords defines (w:dog is also a verb). BEGIN"
BASIC_END = "# Classes WordNet gives words BasicWords defines. END"

# One entry per spelling, with an is-a link for each of its classes (w:good is-a adjective and
# is-a commonNoun): English is overwhelmingly ambiguous, and a word's classes are its parents.
lines = []
entries = {}
classes = defaultdict(list)
basic_lines = []   # classes WordNet gives words BasicWords defines (w:dog is also a verb)
for p in 'nvar':
    for lemma in sorted(lemmas[p]):
        wclass = CLASS.get(p) or ('commonNoun' if lemma in lowercase_noun else 'properNoun')
        if lemma in known:
            spelled_as = labels_spelled[lemma]
            if any(already_has(label, wclass) for label in spelled_as): continue
            bases = [label for label in spelled_as if word_classes[label] - {'wordForm'}]
            basic_lines.append(f'[{(bases or spelled_as)[0]}->is-a->{wclass}]')
            continue
        classes[lemma].append(wclass)
for lemma in sorted(classes):
    label = f'w:{lemma}'
    for p in 'nvar':
        if lemma in lemmas[p]: entries[(lemma, p)] = label
    lines.extend(f'[{label}->is-a->{wclass}]' for wclass in classes[lemma])
    lines.append(f'[{label}->spelled->{spelled(lemma)}]')

# Irregular forms. The inflection must be clear from the spelling; a verb form that could be the
# past or the past participle (went/gone, swam/swum) is skipped. A form is labeled by its spelling,
# so a form that is also a word is the same Thought (w:better: an adjective, a verb, ..., and good's
# comparative).
exceptions = []
skipped = defaultdict(int)
def exception(form, base, p, link, tag):  # tag: unused, kept for reading
    if (base, p) not in entries or not word_re.match(form): skipped['no entry'] += 1; return
    label = f'w:{form}'
    new_lines = [f'[{label}->is-a->wordForm]', f'[{entries[(base, p)]}->{link}->{label}]']
    if form not in classes and form not in known: new_lines.insert(1, f'[{label}->spelled->{spelled(form)}]')
    exceptions.extend(line for line in new_lines if line not in emitted)
    emitted.update(new_lines)
emitted = set()
for name, p in [('noun', 'n'), ('verb', 'v'), ('adj', 'a')]:
    for line in open(f'{dict_dir}/{name}.exc', encoding='utf-8'):
        form, *bases = line.split()
        for base in bases:
            if form == base: continue
            if p == 'n': exception(form, base, p, 'hasPlural', 'pl')
            elif p == 'a':
                if form.endswith('est') or form in ('best', 'worst', 'most', 'least'): exception(form, base, p, 'hasSuperlative', 'est')
                elif form.endswith('er') or form in ('better', 'worse', 'more', 'less'): exception(form, base, p, 'hasComparative', 'er')
                else: skipped['adjective, unclear'] += 1
            else:
                if form.endswith('ing'): exception(form, base, p, 'hasPresentParticiple', 'ing')
                elif form.endswith('s') and not form.endswith('ss'): exception(form, base, p, 'hasThirdPersonSingular', 's')
                else: skipped['verb, past or participle'] += 1

with open(out_path, 'w', encoding='utf-8', newline='\n') as out:
    out.write('''# Vocabulary from WordNet 3.1 (generated by make_wordnet_words.py; do not edit by hand): the
# lemmas that occur in WordNet's sense-tagged corpus, a proxy for common words.
# Loaded by ModuleInflect after BasicWords.txt and Inflections.txt; not by ModuleInterpret.
# Spellings and word classes only: no meanings. One entry per spelling, a parent per class
# (w:bark is-a commonNoun, is-a verb). Spellings BasicWords already has get no entry of their own;
# the classes WordNet adds to them (w:dog is also a verb) are in the first section. A noun WordNet only ever capitalizes
# is a properNoun. Irregular forms come from WordNet's exception lists where the inflection is
# clear from the spelling (plurals, -ing, -er/-est); a verb form that may be the past or the past
# participle (swam, swum) is left out.
#
# WordNet license (from its data files; it must appear on all copies):
''')
    for line in open(f'{dict_dir}/data.noun', encoding='utf-8'):
        if not line.startswith('  '): break
        out.write(('# ' + line[2:].split(' ', 1)[1].rstrip()).rstrip() + '\n')
    out.write('\n')
    # Read on its own by the ambiguity experiment (ModuleInterpret), so it defines adverb itself.
    out.write('\n'.join([BASIC_START, '[adverb->is-a->wordType]'] + basic_lines + [BASIC_END, '']) + '\n')
    out.write('\n'.join(lines + [''] + ['# Irregular forms'] + exceptions) + '\n')
print('BasicWords words given more classes', len(basic_lines), basic_lines)
print('words', len(classes), 'class links', sum(map(len, classes.values())),
      'proper', sum(1 for l in lines if 'properNoun' in l), 'exception lines', len(exceptions), dict(skipped))
