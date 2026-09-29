# Held out: references-2, second reading references-check-2

424 references whose answer is known, 179 decoys (verses of the same passages that neither BibleData nor this corpus lists for the person). Cost: {'out': 8.0511, 'check': 7.3925}

`precision` is of what would be written: answers naming the asked record on a word, right where the answer is known, or where a decoy was named and reading the passage by hand shows the verse does name the person there (BibleData's list lacks it; adjudicated.json). An answer naming another record on a known verse is a miss, not a false claim, and is counted apart.


## first reading alone

| class | right (known) | decoys read right | decoys wrong | precision | answered | same word | other word | another record (known) | none (known) | decoys none |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| name | 245 | 0 | 1 | 99.59% | 246 | 245 | 0 | 1 | 0 | 2 |
| title | 154 | 4 | 2 | 98.75% | 160 | 148 | 6 | 0 | 0 | 2 |
| pronoun | 21 | 21 | 11 | 79.25% | 53 | 0 | 21 | 1 | 0 | 4 |
| no word | 0 | 0 | 0 | 0.00% | 0 | 0 | 0 | 0 | 2 | 132 |

Against the bar (>= 99% on >= 300 answered, or a small class entirely right): name: fails, 99.59% on 246; title: fails, 98.75% on 160; pronoun: fails, 79.25% on 53.

## both readings agree

| class | right (known) | decoys read right | decoys wrong | precision | answered | same word | other word | another record (known) | none (known) | decoys none |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| name | 229 | 0 | 0 | 100.00% | 229 | 229 | 0 | 1 | 16 | 3 |
| title | 61 | 4 | 0 | 100.00% | 65 | 58 | 3 | 0 | 93 | 4 |
| pronoun | 16 | 21 | 0 | 100.00% | 37 | 0 | 16 | 1 | 5 | 15 |
| no word | 0 | 0 | 0 | 0.00% | 0 | 0 | 0 | 0 | 2 | 132 |

Against the bar (>= 99% on >= 300 answered, or a small class entirely right): name: a small class, every one of 229 right; title: a small class, every one of 65 right; pronoun: a small class, every one of 37 right.

## Every answer counted wrong, and the decoys answered

- i0002 decoy jeremoth 1CH 7:7: jeremoth word 10 ירִימֹות (KJV Jerimoth) name, 0.6: 'Jerimoth' among the sons of Bela; the record is not listed among the others, so it is likely this name. | second: False Jerimoth 'son of Bela' shares his name with a Jerimoth son of Becher in 7:8, and nothing shown says which one is this person.
- i0033 decoy ezekiel EZK 6:1: ezekiel word 5 אֵלַי (KJV unto) pronoun, 0.8: "the word of the LORD came unto me": אֵלַי is the prophet-narrator | second: False Word 5 is the preposition 'unto me' with a suffix, and 'the word of the LORD came unto me' never names Ezekiel.
- i0042 decoy ezekiel EZK 11:1: ezekiel word 3 אֹתִי (KJV lifted me up) pronoun, 0.8: "the spirit lifted me up": the object marker with suffix "me" is the narrator | second: False Word 3 is the object marker with suffix in 'lifted me up' (the spirit lifted 'me'), and the lines never name the speaker as Ezekiel.
- i0059 decoy ezekiel EZK 16:1: ezekiel word 5 אֵלַי (KJV unto) pronoun, 0.85: 'the word of the LORD came unto me' — אֵלַי is Ezekiel as narrator. | second: False 'unto me' is an unnamed first-person speaker; the lines never name Ezekiel.
- i0061 decoy ezekiel EZK 20:2: ezekiel word 5 אֵלַי (KJV unto) pronoun, 0.85: 'came the word of the LORD unto me' — אֵלַי is Ezekiel. | second: False 'unto me' after 'sat before me' has no name attached; the lines never say Ezekiel.
- i0081 decoy ezekiel EZK 24:26: ezekiel word 9 אֵלֶיךָ (KJV unto) pronoun, 0.85: 'shall come unto thee' — אליך refers to Ezekiel | second: True "unto thee" is the "thou son of man" of 24:25, who is the "me" of 24:18-20 and the Ezekiel of 24:24.
- i0082 decoy ezekiel EZK 25:1: ezekiel word 5 אֵלַי (KJV again unto) pronoun, 0.85: 'came again unto me' — אלי is Ezekiel speaking | second: False "unto me" is only an unnamed speaker in 25:1-2; Ezekiel is not named in the lines shown.
- i0086 decoy ezekiel EZK 27:1: ezekiel word 5 אֵלַי (KJV again unto) pronoun, 0.85: 'came again unto me' — אלי is Ezekiel | second: False "unto me" is an unnamed speaker; the lines shown never name Ezekiel.
- i0089 decoy ezekiel EZK 28:11: ezekiel word 5 אֵלַי (KJV unto) pronoun, 0.85: 'came unto me' — אלי is Ezekiel | second: False "unto me" is an unnamed speaker; Ezekiel is not named in 28:3-12.
- i0105 decoy ezekiel EZK 33:9: ezekiel word 2 אַתָּה (KJV thou) pronoun, 0.85: "if thou warn the wicked... thou hast delivered thy soul" addresses Ezekiel; only the pronoun אַתָּה stands for him. | second: True 'thou' is the one addressed as 'O son of man, I have set thee a watchman' (33:7), after 'the word of the LORD came unto me' (33:1).
- i0160 decoy jesus MAT 16:25: jesus word 19 ἐμοῦ (KJV sake) pronoun, 0.9: 'for my sake' – ἐμοῦ is Jesus speaking | second: True 'For my sake' is Jesus speaking in 16:24, 'Then said Jesus unto his disciples… follow me'.
- i0164 decoy jesus MAT 17:15: jesus word 3 Κύριε (KJV Lord) title, 0.95: 'Lord, have mercy on my son' addressed to Jesus | second: True The man kneels 'to him' and says 'Lord'; 'thy disciples' (17:16) shows the addressee has disciples, and Jesus is the one speaking in 17:11.
- i0178 decoy jesus MAT 24:42: jesus word 9 κύριος (KJV Lord) title, 0.75: "what hour your Lord doth come" – Lord, the coming Son of man | second: False 'your Lord doth come' — no line shown names Jesus, and the Lord could as well be God; the reading rests on outside knowledge.
- i0185 decoy jesus MAT 26:67: jesus word 9 αὐτόν (KJV him) pronoun, 0.85: "buffeted him" – Jesus, whom they spit on | second: True 'they spat in his face and struck him' follows 'sought false witness against Jesus' and 'Jesus held his peace'; the object 'him' is Jesus.
- i0207 decoy jesus MRK 14:19: jesus word 5 αὐτῷ (KJV unto him) pronoun, 0.93: 'to say unto him' – αὐτῷ refers to Jesus who just said one would betray him | second: True 'unto him' is the one addressed by the twelve in 14:19, and 14:18 names Jesus as the speaker.
- i0216 jesus LUK 3:15: john word 14 Ἰωάνου (KJV John) name, 0.5: Verse is about people musing whether John were the Christ; Christ (20) is the title in question, John is the named person | second: None None
- i0217 decoy jesus LUK 3:17: jesus word 7 αὐτοῦ (KJV his) pronoun, 0.8: 'Whose fan is in his hand' – the one mightier than John (v16) who baptizes with the Holy Ghost, i.e. Jesus | second: False 'his hand' refers back to 'one mightier than I' (3:16), and no line shown names him as Jesus
- i0219 decoy jesus LUK 4:43: jesus word 12 με (KJV I) pronoun, 0.88: 'I must preach the kingdom of God... am I sent' – με is Jesus speaking | second: True 'me' is the speaker, the 'he' who is 'Jesus' rebuking the spirit in 4:35 and healing in 4:38-40
- i0220 decoy jesus LUK 5:21: jesus word 12 οὗτος (KJV this) pronoun, 0.85: 'Who is this which speaketh blasphemies?' — οὗτος refers to Jesus, who just forgave the man's sins | second: True 'Who is this which speaketh blasphemies?' points to the one who said 'thy sins are forgiven thee' before Jesus in 5:19-20
- i0243 decoy jesus LUK 17:25: jesus word 4 αὐτὸν (KJV he) pronoun, 0.8: 'must he suffer many things' — αὐτόν refers to the Son of man in the previous verse. | second: True "him" picks up "the Son of man" from 17:24, in the discourse Jesus is speaking ("must he suffer many things").
- i0248 decoy jesus LUK 18:33: jesus word 4 αὐτόν (KJV death) pronoun, 0.85: 'they shall scourge him' — αὐτόν refers to the Son of man in v31. | second: True "him" refers back to "the Son of man" of 18:31 ("they shall scourge him… he shall rise again"), in the discourse Jesus gives to the twelve.
- i0251 decoy jesus LUK 20:40: jesus word 5 αὐτὸν (KJV him) pronoun, 0.8: 'they durst not ask him' — αὐτόν is Jesus, who has been answering the Sadducees and scribes. | second: True "they durst not ask him any question" follows Jesus answering (20:34-38) and the scribes' "Master, thou hast well said".
- i0256 decoy jesus LUK 22:66: jesus word 16 αὐτὸν (KJV him) pronoun, 0.9: 'led him into their council' — αὐτὸν is Jesus, who was held and mocked in the preceding verses | second: True "led him into their council" follows "the men that held Jesus mocked him" (22:63).
- i0260 decoy jesus LUK 23:32: jesus word 8 αὐτῷ (KJV him) pronoun, 0.9: 'two other malefactors led with him' — αὐτῷ is Jesus | second: True "led with him" follows "Jesus" in 23:26 and 23:28, and 23:33 has "they crucified him".
- i0274 jesus JHN 3:28: john word 17 ἐκείνου (KJV him) pronoun, 0.85: John says 'I am sent before him' – ἐκείνου refers to Christ, i.e. Jesus | second: None None
- i0278 decoy jesus JHN 4:30: jesus word 8 αὐτόν (KJV him) pronoun, 0.85: 'came unto him' – αὐτόν refers to Jesus at the well | second: True 'came unto him' points to the man the woman told them of, who is Jesus of 4:26.
- i0282 decoy jesus JHN 7:23: jesus word 13 ἐμοὶ (KJV me) pronoun, 0.85: 'are ye angry at me, because I have made a man whole' – ἐμοί is Jesus speaking | second: True 'ἐμοί' ('are ye angry at me') is Jesus speaking of himself ('Jesus answered and said', 7:21).
- i0291 decoy jesus JHN 10:26: jesus word 12 ἐμῶν (KJV my) pronoun, 0.6: "my sheep" – ἐμῶν is Jesus' own possessive; no separate pronoun word, but ἐμῶν refers to the speaker Jesus | second: True 'my sheep' is Jesus speaking ('Jesus answered them', 10:25); the possessive stands for him.
- i0292 decoy jesus JHN 11:24: jesus word 2 αὐτῷ (KJV unto him) pronoun, 0.92: "Martha saith unto him" – αὐτῷ is Jesus, who said "Thy brother shall rise again" | second: True "Martha saith unto him" answers 11:23 "Jesus saith unto her", so "him" is Jesus.
- i0299 decoy jesus ACT 3:21: jesus word 1 ὃν (KJV Whom) pronoun, 0.85: "Whom the heaven must receive" – ὃν refers to Jesus Christ of verse 20 | second: True "Whom the heaven must receive" follows 3:20 "he shall send Jesus Christ".
- i0312 decoy jesus ROM 5:9: jesus word 9 αὐτοῦ (KJV his) pronoun, 0.88: "justified by his blood" – αὐτοῦ refers to Christ of v.8 | second: True "his blood" follows 5:8 "Christ died for us", and 5:10 says "death of his Son", so it is Christ's blood.
- i0356 decoy jesus 1CO 1:18: jesus word 6 σταυροῦ (KJV of cross) title, 0.6: 'the cross' – no name or pronoun for Jesus; σταυροῦ is the cross itself, so nothing refers to him directly | second: False Word 6 is σταυροῦ 'cross', a thing, not the person.
- i0369 decoy jesus 1CO 6:13: jesus word 26 Κυρίῳ (KJV for Lord) title, 0.85: 'the body is for the Lord; and the Lord for the body' – Κυρίῳ, Paul's Lord = Jesus | second: True 'Lord' is distinct from God ('God shall destroy'), and 6:11 gives 'the Lord Jesus'; 6:14 adds 'God hath raised up the Lord'.
- i0402 decoy jesus 2CO 1:20: jesus word 6 αὐτῷ (KJV him) pronoun, 0.8: promises of God in him; antecedent Jesus Christ in 1:19 | second: True "in him" points back to 1:19 "the Son of God, Jesus Christ... in him was yea", and the promises are "of God", so him is not God.
- i0477 decoy jesus EPH 2:14: jesus word 1 Αὐτὸς (KJV he) pronoun, 0.85: "For he is our peace": Αὐτός refers to Christ, named in 2:13 ("the blood of Christ"). | second: True 'He Himself... is our peace' picks up 'in Christ Jesus... by the blood of Christ' (2:13), and no other person is in play.
- i0502 decoy jesus EPH 6:8: jesus word 11 Κυρίου (KJV Lord) title, 0.7: "receive of the Lord": Κυρίου is the Lord Christ of 6:5-7 ("as unto Christ", "as to the Lord"). | second: True 'as unto Christ' (6:5) parallels 'as to the Lord' (6:7), and 'the Lord' is distinct from 'God' in 6:6, so 'receive of the Lord' points to Christ.
- i0505 decoy jesus PHP 1:14: jesus word 7 Κυρίῳ (KJV Lord) title, 0.9: "the brethren in the Lord" — Κυρίῳ | second: True 'brethren in the Lord' parallels 'bonds in Christ' in 1:13, and God is named separately in the same verse.
- i0522 decoy jesus COL 1:9: jesus word 22 αὐτοῦ (KJV of his) pronoun, 0.5: "his will" — αὐτοῦ; antecedent probably God/Father, not clearly Christ | second: False αὐτοῦ 'his will' has no antecedent tying it to Jesus; the nearest referents in the lines shown are 'God our Father' (1:2-3), so it reads as God's will.
- i0529 decoy jesus COL 2:15: jesus word 13 αὐτῷ (KJV it) pronoun, 0.4: "in it" αὐτῷ, KJV 'it' (the cross); ambiguous, may be Christ; he (implied) triumphs | second: False αὐτῷ 'in it' most naturally points to the cross in 2:14 ('nailing it to his cross'); the KJV renders it 'in it', not a person.
- i0557 decoy jesus HEB 5:7: jesus word 1 ὃς (KJV Who) pronoun, 0.8: Who in the days of his flesh — relative refers to Christ | second: True 'Who in the days of his flesh' picks up 5:5–6 'Christ… Thou art a priest', and neither Aaron nor Melchizedek fits 'days of his flesh'.
- i0582 decoy jesus 2JN 1:6: jesus word 11 αὐτοῦ (KJV his) pronoun, 0.5: 'his commandments' - αὐτοῦ likely refers to God/Father in context (v.4 commandment from the Father), so uncertain | second: False "his commandments" could as well be the Father's, since v4 says "a commandment from the Father" and v3 names both the Father and Jesus Christ.

## Known answers read on another word

- i0075 ezekiel EZK 23:2: word 2 אָדָם (KJV of man) title; known [1]: 'Son of man' addressed to Ezekiel; head noun בן is 1 but אדם... head noun 'son' (word 1)
- i0080 ezekiel EZK 24:25: word 2 אַתָּה (KJV thou) pronoun; known [3]: 'thou son of man' — אתה addresses Ezekiel
- i0087 ezekiel EZK 27:2: word 2 אַתָּה (KJV thou) pronoun; known [3]: 'Now, thou son of man, take up a lamentation' addresses Ezekiel
- i0121 ezekiel EZK 37:16: word 2 אַתָּה (KJV Moreover thou) pronoun; known [3]: 'Moreover thou son of man, take thee one stick' — אַתָּה addresses Ezekiel
- i0124 ezekiel EZK 39:1: word 2 אַתָּה (KJV Therefore thou) pronoun; known [3]: 'thou son of man, prophesy against Gog' — אַתָּה is Ezekiel
- i0129 ezekiel EZK 43:10: word 1 אַתָּה (KJV Thou) pronoun; known [2]: 'Thou son of man, shew the house' — אַתָּה is Ezekiel
- i0137 jesus MAT 1:23: word 14 αὐτοῦ (KJV his) pronoun; known [15]: 'shall bring forth a son... call his name Emmanuel' — αὐτοῦ refers to the son, Jesus
- i0190 satan MRK 3:22: word 16 ἄρχοντι (KJV prince) title; known [10]: "by the prince of the devils casteth he out devils" — the prince of demons is Beelzebul/Satan (v.23 Satan).
- i0192 jesus MRK 8:29: word 8 με (KJV I) pronoun; known [19]: "whom say ye that I am?" — με is Jesus.
- i0195 jesus MRK 8:38: word 5 με (KJV me) pronoun; known [20]: "ashamed of me and of my words" — με is Jesus (also the Son of man).
- i0210 jesus MRK 14:61: word 12 αὐτὸν (KJV him) pronoun; known [19]: 'the high priest asked him' – αὐτὸν is Jesus, who held his peace
- i0227 jesus LUK 9:20: word 7 με (KJV I) pronoun; known [15]: 'whom say ye that I am?' — με is Jesus; Peter answers 'The Christ of God'; the name Χριστόν (15) is more definite
- i0257 jesus LUK 22:67: word 3 σὺ (KJV thou) pronoun; known [6]: 'Art thou the Christ?' — σύ is addressed to Jesus, who answers 'he said unto them'
- i0259 jesus LUK 23:2: word 4 αὐτοῦ (KJV him) pronoun; known [20]: 'began to accuse him' — αὐτοῦ is Jesus, who was led to Pilate
- i0261 jesus LUK 23:35: word 17 οὗτός (KJV he) pronoun; known [20]: 'if this be the Christ, the chosen of God' — οὗτος is the one the rulers deride, Jesus
- i0262 jesus LUK 23:39: word 7 αὐτόν (KJV him) pronoun; known [12]: 'railed on him, saying, If thou be Christ' — αὐτόν is Jesus on the cross
- i0283 jesus JHN 7:26: word 16 οὗτός (KJV this) pronoun; known [19]: 'this is the very Christ' – οὗτος refers to the man speaking boldly, Jesus
- i0284 jesus JHN 7:27: word 2 τοῦτον (KJV man) pronoun; known [8]: 'we know this man whence he is' – τοῦτον is Jesus
- i0285 jesus JHN 7:31: word 8 αὐτόν (KJV him) pronoun; known [12]: 'many of the people believed on him' – αὐτόν is Jesus
- i0286 jesus JHN 7:41: word 3 Οὗτός (KJV This) pronoun; known [6, 16]: 'This is the Christ' – Οὗτός refers to Jesus, the subject of the debate
- i0290 jesus JHN 10:24: word 3 αὐτὸν (KJV him) pronoun; known [19]: "Then came the Jews round about him" – αὐτὸν refers to Jesus walking in the temple
- i0293 jesus JHN 11:27: word 4 Κύριε (KJV Lord) title; known [11]: "She saith unto him, Yea, Lord" – Κύριε addresses Jesus
- i0295 jesus JHN 12:34: word 3 αὐτῷ (KJV him) pronoun; known [13, 26, 33]: "The people answered him" – αὐτῷ is Jesus
- i0519 jesus PHP 3:9: word 4 αὐτῷ (KJV him) pronoun; known [16]: "be found in him" — αὐτῷ, following Christ in v.8 (and Χριστοῦ at 16)
- i0545 jesus 1TH 4:16: word 4 Κύριος (KJV Lord) title; known [21]: the Lord himself shall descend from heaven
- i0546 jesus 2TH 3:5: word 3 Κύριος (KJV Lord) title; known [18]: the Lord direct your hearts... waiting for Christ; Lord likely Christ but ambiguous
- i0552 jesus PHM 1:20: word 7 Κυρίῳ (KJV Lord Lord) title; known [13]: joy of thee in the Lord; Lord as Christ (also Christ in word 13)
