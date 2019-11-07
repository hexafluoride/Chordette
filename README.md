# Chordette
Simplified implementation of the [Chord DHT model](https://pdos.csail.mit.edu/papers/chord:sigcomm01/chord_sigcomm.pdf) for learning purposes.

## Features

- [x] All Chord primitives as described in the [whitepaper](https://pdos.csail.mit.edu/papers/chord:sigcomm01/chord_sigcomm.pdf)
- [x] Actual networking
- [ ] A higher layer to Chord, handling things like key handoff and abstraction of the `successor(hash(key)).{get, put}(hash(key))` chain
- [ ] Data duplication
- [ ] Virtual nodes

## Performance metrics
The following metrics all scale with log(N) where N = peer count, matching the performance guaranteed laid out in the [Chord whitepaper](https://pdos.csail.mit.edu/papers/chord:sigcomm01/chord_sigcomm.pdf), which corroborates the correctness of this implementation to some degree.

![Milliseconds spent per query](https://github.com/hexafluoride/Chordette/blob/master/docs/metrics/time-per-query.png?raw=true)

![Bytes on wire per query](https://github.com/hexafluoride/Chordette/blob/master/docs/metrics/bytes-per-query.png?raw=true)

![Messages sent per query](https://github.com/hexafluoride/Chordette/blob/master/docs/metrics/msgs-per-query.png?raw=true)

## Screenshots

![Chordette immediately after boot, in the process of stabilization](https://github.com/hexafluoride/Chordette/blob/master/docs/screenshots/chordette-1.PNG?raw=true)

_Chordette immediately after boot, in the process of stabilization_

![Chordette a few minutes after boot, when stabilization is complete](https://github.com/hexafluoride/Chordette/blob/master/docs/screenshots/chordette-2.PNG?raw=true)

_Chordette a few minutes after boot, when stabilization is complete_

## License
Chordette is licensed under MIT.
