# Chordette
Simplified implementation of the [Chord DHT model](https://pdos.csail.mit.edu/papers/chord:sigcomm01/chord_sigcomm.pdf) for learning purposes.

## Screenshots

![Chordette immediately after boot, in the process of stabilization](https://github.com/hexafluoride/Chordette/blob/master/docs/screenshots/chordette-stabilization.PNG?raw=true)

_Chordette immediately after boot, in the process of stabilization_

![Chordette a few minutes after boot, when stabilization is complete](https://github.com/hexafluoride/Chordette/blob/master/docs/screenshots/chordette-stabilization-2.PNG?raw=true)

_Chordette a few minutes after boot, when stabilization is complete_

## Features

- [x] All Chord primitives as described in the [whitepaper](https://pdos.csail.mit.edu/papers/chord:sigcomm01/chord_sigcomm.pdf)
- [ ] Actual networking
- [ ] A higher layer to Chord, handling things like key handoff and abstraction of the `successor(hash(key)).{get, put}(hash(key))` chain
- [ ] Data duplication
- [ ] Virtual nodes

## License
Chordette is licensed under MIT.
