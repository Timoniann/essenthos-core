"""Finish the Spanish Strong translation from the frozen Codex run, asking Codex only for reviewed repairs."""
import argparse
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('controller', HERE / 'generate-strong-codex.py')
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
ORIGIN = base.ROOT / 'es'
RUN = base.MAIN / 'Resources/Essenthos/generation/strong-es-finish-codex-2026-10-21-v1'
CODE = ('lexicon.py', 'lexicon-codex.py', 'generate-strong-codex.py', 'finish-strong-es-codex.py')
INPUTS = ('full/source.json', 'full/manifest.json', 'full/batches', 'pilot/source.json',
          'pilot/manifest.json', 'pilot/batches', 'pilot/answers.jsonl', 'generation-readiness.json')

# The raw answer each unanswered entry was read in, bound to the bytes that were read.
REVIEWED_RECOVERIES = {
    'G107': ('79e08eec851f475dbbe9194c8ab1ecf5/450ad3bd21f94558820424d883621444.json', '174f848fb2fca8dcb24e553cef1c4b4ba2da50b7297b2447d381bf6dbe27eedd'),
    'G278': ('79e08eec851f475dbbe9194c8ab1ecf5/23f7d71347ff4a8c9ce5a03a1165f170.json', '802374aab526fbcd4891a749fbc63ae87832b69fc65a4aa5f0eb358c9befebd9'),
    'G448': ('79e08eec851f475dbbe9194c8ab1ecf5/c81e1e6c4fc048f7828811e0af55036b.json', '3a9d209c0669adda60911f6f76851f95e812905d09ec8bffafbebcaf2419d0e1'),
    'G517': ('79e08eec851f475dbbe9194c8ab1ecf5/ac4423887bff480698ea993c073c9f81.json', '8fe53620e9121d7c6e5298beacf2de64cdf85d89ccb40f22bfcb46bb1861fb01'),
    'G896': ('79e08eec851f475dbbe9194c8ab1ecf5/e9572fdc83d640fa81a44c7d21eda9df.json', 'b0df582223baf378b91407ccae7680ee944fb551f7ba1661adc3d8f0a6c4450d'),
    'G1857': ('79e08eec851f475dbbe9194c8ab1ecf5/c54de0ac432e43deb83f5e96ecddb388.json', '1b8e03cf4aaeeaf7655771ae67fbbe127ec928bdfe753d8684acc87c2878b3ed'),
    'G2082': ('79e08eec851f475dbbe9194c8ab1ecf5/798a113faf084018bc46358148ae196c.json', '7c5dc28f07c26946786701dc51fa3a6924749a9d9acdca618faa0c48c8bfcf9f'),
    'G3165': ('79e08eec851f475dbbe9194c8ab1ecf5/884c99b243174f8999584e1e81f9e383.json', '757682b2b749cc24dd532bc747aa257d99bfb97ae4e72afdb0b8bb7671b601f7'),
    'G3711': ('79e08eec851f475dbbe9194c8ab1ecf5/9d2366b3673c404ebed0de255087ce59.json', '01bbefc014588a04eae302efca540e279c4d25e512017b2ff71fb25624ab59c1'),
    'G4324': ('79e08eec851f475dbbe9194c8ab1ecf5/cb203435241640fe83e14b950177897b.json', '82976028714f0a660ac1443227d074e67a38ad5e8ae0a46222a5e58d9b4516ee'),
    'G4427': ('79e08eec851f475dbbe9194c8ab1ecf5/c055d58d909f44798a7176e056af1a8f.json', '6569527dd8e812f62b42ce00f6baf6dfbf761d7962d0cc2749610fc9c5888ca7'),
    'H394': ('79e08eec851f475dbbe9194c8ab1ecf5/50491d0d31454dadabc3fe23e6708fd5.json', '9ea0e841612f92da536d6b56c5618ad0c1fdf508a150ef017e14085d3a5e4daf'),
    'H520': ('79e08eec851f475dbbe9194c8ab1ecf5/548700ec3c7a48c89d3448978b3eb155.json', '3f1242caab17a9b1764387a205b640d28890c4988a1cfa989f8e99f29561c779'),
    'H574': ('79e08eec851f475dbbe9194c8ab1ecf5/fd514e0b168d4c928fd08a3f7e4b12ff.json', '730bd11678b40807ba7b117b4e5593a725e5576bcb8e2b86be236a46b81ab987'),
    'H609': ('79e08eec851f475dbbe9194c8ab1ecf5/5700eeed6683428698415683b886cea3.json', '2b2c8c9e5c455e79b9dd0d59b008ef1451693e137f4b79a4ddebe7f9042e3eed'),
    'H945': ('79e08eec851f475dbbe9194c8ab1ecf5/24c97e56b4014eda98e37106fdda1d40.json', '4904beb0e97de11876fddae0293c589b38898732ac754662e1401d5b0b1c87f5'),
    'H1150': ('79e08eec851f475dbbe9194c8ab1ecf5/195a27e9c7544deebc32ed710faa9d8c.json', '9d9dd80ed7e4b6d69081a29da42546f8f751bfc360313e196043a744c3b37ee1'),
    'H1153': ('79e08eec851f475dbbe9194c8ab1ecf5/195a27e9c7544deebc32ed710faa9d8c.json', '9d9dd80ed7e4b6d69081a29da42546f8f751bfc360313e196043a744c3b37ee1'),
    'H1609': ('79e08eec851f475dbbe9194c8ab1ecf5/21d1e664f3264263a1730e3bd982cdb0.json', '13e27113b8501772ae1865bc0a4bbddcf840a38d9f9b56e16694ee1edcb8a304'),
    'H1835': ('79e08eec851f475dbbe9194c8ab1ecf5/24383be626fb489da165f4e755a97b9c.json', '1093993fbb40820c6a844dc3699698ddb8b4c56c72917086f5df825fb0041ef2'),
    'H2371': ('79e08eec851f475dbbe9194c8ab1ecf5/a64895ec5d91440a86a3945325c3e4b6.json', '3733d84f886324adaf85c200c3a31fec06bdab383ff854c90667cea56e1609d9'),
    'H2618': ('79e08eec851f475dbbe9194c8ab1ecf5/eae5106599f246e6a6ca9d7f195c7edb.json', '30a537787c42e922a61b7e36cd48408ebdfe8dd4b61c698d79eb30013a213d1d'),
    'H2849': ('79e08eec851f475dbbe9194c8ab1ecf5/42c316b2d8d94b04b58c4405d4dd14d5.json', '63d3bc0dee8bfdb90b667a41424122ac6b8c3de1e25d122fc96e3a8e51e35761'),
    'H2851': ('79e08eec851f475dbbe9194c8ab1ecf5/42c316b2d8d94b04b58c4405d4dd14d5.json', '63d3bc0dee8bfdb90b667a41424122ac6b8c3de1e25d122fc96e3a8e51e35761'),
    'H2866': ('79e08eec851f475dbbe9194c8ab1ecf5/df1acf4a7b3443cab453f084ded59eb7.json', 'da0f77d854e78aae77fb93b414e1dd976f395a4f4440cadbe9cbc068ffdd51a1'),
    'H3035': ('79e08eec851f475dbbe9194c8ab1ecf5/9f4dfb3fcf92473ab8f1554f3a2f8e53.json', 'e49a5c298cef41d9243d5b57b548c7de4a60183749b330606b9a597153fda42a'),
    'H3069': ('79e08eec851f475dbbe9194c8ab1ecf5/a6f0fe41f36445dbb511a9efe7254ae5.json', '8c5e03b7f0e1a4fc734e5384431f124bdda8a994f11802e1e2e3cd0b9440a2e8'),
    'H3434': ('79e08eec851f475dbbe9194c8ab1ecf5/ddaa74b4e36545e8bad36dc1cb965b67.json', 'b2f2849cce69b67292ae8878e116145db3f5e0f9a8fca3bb3ebafb39eff81bfc'),
    'H3695': ('79e08eec851f475dbbe9194c8ab1ecf5/9712b0b8f5704d89a181958d352c63c4.json', '0adf6ff1b56750ff4f8943b7eb189b376e00fbdd3cbd27690793d129622d0562'),
    'H3942': ('79e08eec851f475dbbe9194c8ab1ecf5/f2c8249182eb4c4899ec0ea246d99613.json', '271409f8b890dc1f7e75f31f505424c8de304caca4e8f9c6321549b61bf035bd'),
    'H4025': ('79e08eec851f475dbbe9194c8ab1ecf5/1f0ccd2fc4d7452d81ff23ed9a031e15.json', '0795de03b7c3d9c648305a96054cbb546e08d4e4274ff1a6fe06e9fb7164906c'),
    'H4027': ('79e08eec851f475dbbe9194c8ab1ecf5/1f0ccd2fc4d7452d81ff23ed9a031e15.json', '0795de03b7c3d9c648305a96054cbb546e08d4e4274ff1a6fe06e9fb7164906c'),
    'H4028': ('79e08eec851f475dbbe9194c8ab1ecf5/1f0ccd2fc4d7452d81ff23ed9a031e15.json', '0795de03b7c3d9c648305a96054cbb546e08d4e4274ff1a6fe06e9fb7164906c'),
    'H4056': ('79e08eec851f475dbbe9194c8ab1ecf5/b78fc48cb19a4b3cb1056f282b65161d.json', '29047e558857f6935bcf9c4bfddd886fc0244bc936eccfcfbd22f07d4d083992'),
    'H4196': ('79e08eec851f475dbbe9194c8ab1ecf5/9ab5d7fb58fe4850b40f3136d06ecfaa.json', '43f29f9cac382be042b887d1697023809c82dec4a310427723e96778e54964fb'),
    'H4879': ('79e08eec851f475dbbe9194c8ab1ecf5/4f447780c35e49699e41ed4a3563561d.json', '02273e9cf2db7d7b01e4d82adc98f6a6eb43a3de88310e53441c400533539170'),
    'H5113': ('79e08eec851f475dbbe9194c8ab1ecf5/4961bc7e91804b6bbaddc4bafc0b1001.json', 'ae10a5329b74a8487ab8dfbc3f6cc42b797d2431ceddab14b2f0a7e64278c946'),
    'H5114': ('79e08eec851f475dbbe9194c8ab1ecf5/4961bc7e91804b6bbaddc4bafc0b1001.json', 'ae10a5329b74a8487ab8dfbc3f6cc42b797d2431ceddab14b2f0a7e64278c946'),
    'H5334': ('79e08eec851f475dbbe9194c8ab1ecf5/f39ea91d02d94375bb2a02555decd649.json', '2dcc392b8f7707a3a4e30027a2dd8eb6d0054fb7c9298c33bc3071de3922e5a3'),
    'H5512': ('79e08eec851f475dbbe9194c8ab1ecf5/6881ff0fbb0b405f895ae0bb36b76665.json', 'f3532cd34fd7fb67cabb18b2350beb0a7ba979e3d392ba560be1ff7dd91ef0de'),
    'H5515': ('79e08eec851f475dbbe9194c8ab1ecf5/6881ff0fbb0b405f895ae0bb36b76665.json', 'f3532cd34fd7fb67cabb18b2350beb0a7ba979e3d392ba560be1ff7dd91ef0de'),
    'H5540': ('79e08eec851f475dbbe9194c8ab1ecf5/b440b560be0c4e47b682aafb949516d2.json', 'a2db7559225bcb3b3187d5101607247a025c4fe6030904cd6e2f8a2d00e4186e'),
    'H5617': ('79e08eec851f475dbbe9194c8ab1ecf5/120697812cc1426087b10d8413fd6190.json', 'b4958e1e1288c15ed4bd3c5cf42beeaa921a1412737ef3bfa2630210340f2d84'),
    'H5653': ('79e08eec851f475dbbe9194c8ab1ecf5/7709487c1c7b493da93a6019023c968a.json', '097d0883d55c090415aed11caf22bb8d5a618e3b3908abea1ffc4319faf0ed9d'),
    'H5681': ('79e08eec851f475dbbe9194c8ab1ecf5/c00d73380cb14804a226d1111f73dd3b.json', 'a0d6f45b0fa86a2809865558686a8e970362e581c2c967d0db5f3f8a790f1e79'),
    'H5696': ('79e08eec851f475dbbe9194c8ab1ecf5/09554affcc044cae95f7934c7d8e1bf1.json', '6a37eaf1333f32371983f2047811d63d01e8ea117135cc0b0fc5c686ebcd3977'),
    'H5717': ('79e08eec851f475dbbe9194c8ab1ecf5/3511724e4cd34722ac9e3ad39950bc0f.json', 'b977c031baf44bce6501e0e65f62c8d3a45e81fb37a50544b12f7c578c46e5a0'),
    'H5733': ('79e08eec851f475dbbe9194c8ab1ecf5/90c67025f0bf4474aff7c309d76a045e.json', '2bab17cbd7b549e4999c31d67a1527508e291f72ecf203723331fb17922dc242'),
    'H6036': ('79e08eec851f475dbbe9194c8ab1ecf5/12e39af1c96742419b7f98db7381b83c.json', '40ea37eaa7d35adae161f6e833ad059e05bbdaa6f13189717be067bbee5742a6'),
    'H6063': ('79e08eec851f475dbbe9194c8ab1ecf5/3e5b71742bb94e6191a60c1a478bf65b.json', '3aae952c4e3a57c8164fbcdb2dd85da974a9a8fba25e90c778b2662669daf758'),
    'H6088': ('79e08eec851f475dbbe9194c8ab1ecf5/fe9ca32c9c874311b067d4a131193e35.json', 'd3cf74ceb65f11ae1c35bdc73a3444bfa57391c508edba588b50d29368eff8d7'),
    'H6107': ('79e08eec851f475dbbe9194c8ab1ecf5/0f09d3a388c74d4b9c0f683329603b56.json', '711d6929a99e90170439b87b50972aaf3569dc6a5038d513b67baadc246362c7'),
    'H6221': ('79e08eec851f475dbbe9194c8ab1ecf5/f5d9ff32d47640ddb1884e4875fc61e4.json', '785f6e1e0588148d1635ed52a7dfe49b19e4bc3f06a4cd2cae18229db98a8ba1'),
    'H6268': ('79e08eec851f475dbbe9194c8ab1ecf5/6b1331031eb44edba8f68bbc4389ba44.json', '5db127e70765fe80791b53448dd91198d03f67ea8ffd6f3d7aa381a937b0176a'),
    'H6317': ('79e08eec851f475dbbe9194c8ab1ecf5/3dfaadbdffbf466b87cd7a959b487228.json', 'c71f3736a158f374fcd15de82ad2dc9dbffda430873a1f5ea76e7d97b3481b38'),
    'H6332': ('79e08eec851f475dbbe9194c8ab1ecf5/376afef59e294c00a07fa315e3aadf93.json', 'e004c74c39fdd2649f8bc859f4bec29322c50c84b8780c8d669a3c071d194b68'),
    'H6405': ('79e08eec851f475dbbe9194c8ab1ecf5/a170b0ec59a64ac88aed13b23dd5cfa0.json', '3c4eb47a487cb716d4f1cc7c24987c38a02a66178b6dc815a21539f53bdb5670'),
    'H6416': ('79e08eec851f475dbbe9194c8ab1ecf5/720c977722954a9dbbbdad608cfebaf4.json', '049a989c85a6a62bfbcb286bf2b92899bf29d83cb40c9e3023d84ade2219a556'),
    'H6442': ('79e08eec851f475dbbe9194c8ab1ecf5/bc8dd4ebf8d942fc8f83e236464a91d8.json', '18b7a6098c72634c5ae9ff3743974f538de0f8dd48ed5ef6afa8631ce838b7f3'),
    'H6464': ('79e08eec851f475dbbe9194c8ab1ecf5/798d70a279f54278990ba24cb6990db0.json', 'c6c1b3cceb1b8939079a946ef23837ba1c12337f3ea45fcb44291181d9d2b30e'),
    'H6514': ('79e08eec851f475dbbe9194c8ab1ecf5/6c8d1fe39f3d4bd2af84fa3e9646e9d1.json', 'b747f4f0425f387b883786938263e70a867162356548a83d2631c8331a3b8fe3'),
    'H6516': ('79e08eec851f475dbbe9194c8ab1ecf5/6c8d1fe39f3d4bd2af84fa3e9646e9d1.json', 'b747f4f0425f387b883786938263e70a867162356548a83d2631c8331a3b8fe3'),
    'H6559': ('79e08eec851f475dbbe9194c8ab1ecf5/c320b6bbaac64ba0bfb6f2d94d425b4f.json', 'b3cc690860a1bc65db566e41c133c139b0210fc816aa20e9f467f8d0dc1a0778'),
    'H6615': ('79e08eec851f475dbbe9194c8ab1ecf5/73944432d3a944dca7cca8d0fd7ddbc7.json', '96854da28f61b5f9b2fe00572fa8c1c5a52e118ab9be6a1953290ccd3e1ab349'),
    'H6727': ('79e08eec851f475dbbe9194c8ab1ecf5/68a1ca15420249d6981900f09dddbde5.json', 'd144095aa12487cd8a603096f71f59dd6e15f2530e2098d09f3cc7ecfae9f9e9'),
    'H6730': ('79e08eec851f475dbbe9194c8ab1ecf5/68a1ca15420249d6981900f09dddbde5.json', 'd144095aa12487cd8a603096f71f59dd6e15f2530e2098d09f3cc7ecfae9f9e9'),
    'H6811': ('79e08eec851f475dbbe9194c8ab1ecf5/88c3c8c36bc3460b8d98d565c59f455f.json', '588b526b51582c284405460da730394303051a7b8bb1f8ef65513b38ccf2e7b8'),
    'H6820': ('79e08eec851f475dbbe9194c8ab1ecf5/a4ed26d266b646938ccb070c9272afd3.json', '85688749f26b874f05d3297ce2f1313830c009054a75bd301f96dd584a8700e1'),
    'H6988': ('79e08eec851f475dbbe9194c8ab1ecf5/62709aabb2aa4bc3b0ac2a3d4dd19236.json', 'f5f43609e4b033958f71709f88c4e8fffce12e3e7670d2b0303f764bc1a888ae'),
    'H7002': ('79e08eec851f475dbbe9194c8ab1ecf5/e756a15bb58c4ba58b692428fd5ac96b.json', '76b07796acc98f25f59b708a06f084ae2395e1aa304e7d2a0aa00dbc9795faf1'),
    'H7020': ('79e08eec851f475dbbe9194c8ab1ecf5/235170a149cd4319a6c63d5c2b10ecf9.json', 'b2cd1105695d6e1a054371d277c6c6d2a5df49f67ebc50c70c996249ec44835a'),
    'H7254': ('79e08eec851f475dbbe9194c8ab1ecf5/e7615b3857194174bbb0056c0996c684.json', 'a4d18583229ef3d3870b18a3711312d6fe4ff5c87875032a12685fc040b235ce'),
    'H7357': ('79e08eec851f475dbbe9194c8ab1ecf5/3442fa77e3824c52856f30cc549e566c.json', '400707fb8cd89a86ecfb33b768d38c52db609c7010dda9f3b3361f3e41db0d26'),
    'H7374': ('79e08eec851f475dbbe9194c8ab1ecf5/8582ded3680347e89444a7c1fcc521ef.json', '271898ad5cf9245afa0705cc052da3ed7be79278682b81057526ae184cbbc1b8'),
    'H7380': ('79e08eec851f475dbbe9194c8ab1ecf5/8582ded3680347e89444a7c1fcc521ef.json', '271898ad5cf9245afa0705cc052da3ed7be79278682b81057526ae184cbbc1b8'),
    'H7466': ('79e08eec851f475dbbe9194c8ab1ecf5/a10e1019e43545898d47afd45224c688.json', '16262abe4f3c4cef9622afeff41887dcd0475239de21cb47cd73eaf47291a615'),
    'H7578': ('79e08eec851f475dbbe9194c8ab1ecf5/56a9604676544ae387d2fa65f4a80264.json', '56feaa9cc86a3206fdf4db5746416bff13ed4de9f50e569e6274d05601665274'),
    'H7691': ('79e08eec851f475dbbe9194c8ab1ecf5/5ccbc4fc9577417fb40e83eb1e9865b1.json', '8c2c10adeb7535026623024188ef0edce97410a5b6b8903d2cd219ac808a995d'),
    'H7755': ('79e08eec851f475dbbe9194c8ab1ecf5/5175b72cc1244747b762fccce86d86fe.json', '49796d9e46499e052206baff105e3a2c43169d867462a90914bc7a722a9d8b1d'),
    'H8004': ('79e08eec851f475dbbe9194c8ab1ecf5/a2e2a3ef432f498ab4bd8a98f6e8179a.json', '121ef9ea5b4559cee7668d098f3708f90faee4c121249953d3455a8252abd8de'),
    'H8007': ('79e08eec851f475dbbe9194c8ab1ecf5/341ebf8967004153ae6673a807a153a0.json', '5643e0a34d223fae4230f2306a9db26cb90c0240f5ef706ff59eb76f2fbc5f57'),
    'H8286': ('79e08eec851f475dbbe9194c8ab1ecf5/7c007c8c1f474e7aa8ce802ecf6a7c3b.json', '2d85aefa1aa2fe2f14b5bac403de944774a67d52b8c1325540e577cc63cb9199'),
    'H8396': ('79e08eec851f475dbbe9194c8ab1ecf5/23971fd3ff984f03baf8993a04bd995d.json', '0fb7bc1a86906522eec1deb0e18521d470f61f13c0b06d69b1203bc49562140f'),
    'H8417': ('79e08eec851f475dbbe9194c8ab1ecf5/cfdb44e1f3474ac885408ca48b80cfe6.json', '22fac8049960b9201d025b2ac0311c9f7a03a926ce9091efb10b3d85733ad671'),
    'H8459': ('79e08eec851f475dbbe9194c8ab1ecf5/fad6e611e97c4985aa0cb78bc64efa03.json', '650df9fc6d5c8336d6aa727c99235c8480edcf071ce12732862778210f811677'),
    'H8484': ('79e08eec851f475dbbe9194c8ab1ecf5/454cb3cc503d47cfb388fd652ea6e025.json', '00eaa664498d4d5e8396ffa4c5c58bfb7a099657265c0006151309d4fa7976a1'),
    'H8488': ('79e08eec851f475dbbe9194c8ab1ecf5/8508729faf494164996ba4d54c8c7abf.json', 'f205f8e57c010fe9d180a390ec635755755120f1fdc22bbd2f519dccd406e4a8'),
    'H8559': ('79e08eec851f475dbbe9194c8ab1ecf5/f8a0b2c23aff4af2a658c494cfd32165.json', '81b31f5583b7e781a14e518cf8a29780388e13f04958e6971d73807115f8b9a0'),
}


def raw_totals(calls):
    usage, records, failed = Counter(), Counter(), Counter()
    for path in sorted(calls.glob('*/*.json')):
        record = base.read_json(path)
        invocation = path.parent.name
        records[invocation] += 1
        outcome = record.get('outcome')
        if record.get('returncode') != 0 or record.get('error') or not isinstance(outcome, dict):
            failed[invocation] += 1
        for key, value in ((outcome or {}).get('usage') or {}).items():
            if isinstance(value, int):
                usage[key] += value
    return {'records': sum(records.values()), 'failed_records': sum(failed.values()),
            'per_invocation': {name: {'records': count, 'failed': failed[name]}
                               for name, count in sorted(records.items())},
            'usage': dict(usage), 'total_cost_usd': None}


def prepare():
    if (RUN / 'frozen.json').exists():
        base.frozen(RUN)
        print('Frozen finish run already exists and matches; nothing replaced.')
        return
    if RUN.exists():
        raise RuntimeError('An incomplete finish run exists; inspect it before replacing anything.')
    base.frozen(ORIGIN)
    (RUN / 'code').mkdir(parents=True)
    for filename in CODE:
        shutil.copy2(HERE / filename, RUN / 'code' / filename)
    for relative in INPUTS:
        source, dest = ORIGIN / relative, RUN / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        (shutil.copytree if source.is_dir() else shutil.copy2)(source, dest)
    gate = base.read_json(RUN / 'generation-readiness.json')
    if gate['answers_sha256'] != base.digest(RUN / 'pilot/answers.jsonl'):
        raise RuntimeError('The reviewed Spanish pilot hash does not match')
    files = {str(path.relative_to(RUN)).replace('\\', '/'): base.digest(path)
             for path in RUN.rglob('*') if path.is_file()}
    base.write_json(RUN / 'frozen.json', {'model': base.MODEL, 'language': 'es',
                    'source_sha256': base.digest(RUN / 'full/source.json'), 'files': files})
    shutil.copy2(ORIGIN / 'full/answers.jsonl', RUN / 'full/answers.jsonl')
    base.write_json(RUN / 'retained-provenance.json', {
        'origin': str(ORIGIN),
        'answers': {'path': str(ORIGIN / 'full/answers.jsonl'),
                    'sha256': base.digest(ORIGIN / 'full/answers.jsonl')},
        'origin_report': base.read_json(ORIGIN / 'full/report.json'),
        'origin_status': base.read_json(ORIGIN / 'status.json'),
        'origin_raw_calls': raw_totals(ORIGIN / 'full/calls'),
        'note': ('Accepted rows of the frozen Codex run retained unchanged with their own provenance; '
                 'the report above counts only invocations that wrote a summary, the raw totals count '
                 'every retained call record. New calls are pinned Codex and recorded under full/calls.')})
    base.state(RUN, 'prepared', language='es', requested=base.EXPECTED)


def recover():
    base.frozen(RUN)
    module = base.module_at(RUN / 'code').lexicon
    folder = RUN / 'full'
    initial = base.digest(folder / 'answers.jsonl')
    sources = {row['strong_number']: row for row in base.read_json(folder / 'source.json')}
    stored = {row['strong_number'] for row in module.read(str(folder / 'answers.jsonl'))}
    recovered, pilot_rows = [], []
    for number, (relative, expected_sha) in REVIEWED_RECOVERIES.items():
        if number in stored:
            continue
        path = ORIGIN / 'full/calls' / relative
        if base.digest(path) != expected_sha:
            raise RuntimeError('Reviewed raw record changed: ' + relative)
        record = base.read_json(path)
        outcome = record.get('outcome')
        provenance = record.get('provenance') or {}
        if (not isinstance(outcome, dict) or record.get('returncode') != 0 or record.get('error')
                or provenance.get('phase') != 'ask'):
            raise RuntimeError('Reviewed raw record is not a completed translation: ' + relative)
        for entry in json.loads(record['prompt'])['entries']:
            asked = entry['strong_number']
            if any(entry.get(field) != sources[asked].get(field)
                   for field in module.PROSE if sources[asked].get(field)):
                raise RuntimeError('Raw prompt does not match the frozen source: ' + relative)
        answers = [row for row in module.parse(outcome.get('result')) or []
                   if isinstance(row, dict) and row.get('strong_number') == number]
        if len(answers) != 1:
            raise RuntimeError(number + ': the reviewed raw record does not hold exactly one answer')
        answer = answers[0]
        failures = module.failures_of(sources[number], answer, 'es')
        if failures:
            raise RuntimeError(number + ': reviewed raw answer fails the checker: ' + str(failures))
        model = next(iter(outcome.get('modelUsage') or {}), None)
        if model != base.MODEL or not provenance.get('run'):
            raise RuntimeError(number + ': cannot recover an uncredited or substituted model result')
        row = {field: answer[field] for field in module.PROSE if sources[number].get(field)}
        row.update(strong_number=number, language='Spanish', method='model-translation', model=model,
                   prompt_version=provenance['prompt_version'], run=provenance['run'],
                   batch=provenance['batch'], effort=record.get('effort'),
                   uncertain=answer.get('uncertain') or [], recovered_from=str(path),
                   recovered_raw_sha256=expected_sha)
        recovered.append(row)
    full = {row['strong_number']: row for row in module.read(str(folder / 'answers.jsonl'))}
    for row in module.read(str(RUN / 'pilot/answers.jsonl')):
        number = row['strong_number']
        current = full.get(number)
        if current and current.get('retained_reviewed_pilot'):
            continue
        if module.failures_of(sources[number], row, 'es'):
            raise RuntimeError(number + ': reviewed pilot answer fails the checker')
        pilot_rows.append({**row, 'retained_reviewed_pilot': True,
                           'retained_from': str(RUN / 'pilot/answers.jsonl')})
    if base.digest(folder / 'answers.jsonl') != initial:
        raise RuntimeError('Answers changed during recovery; wait for the writer to finish and retry')
    with (folder / 'answers.jsonl').open('a', encoding='utf-8') as stream:
        for row in [*recovered, *pilot_rows]:
            stream.write(json.dumps(row, ensure_ascii=False) + '\n')
    base.write_json(RUN / 'recovery-report.json', {
        'recovered': len(recovered), 'recovered_numbers': [row['strong_number'] for row in recovered],
        'reviewed_pilot_rows': len(pilot_rows),
        'initial_answers_sha256': initial, 'final_answers_sha256': base.digest(folder / 'answers.jsonl'),
        'validator_sha256': base.digest(RUN / 'code/lexicon.py')})
    result = base.report(RUN, folder)
    print('Recovered reviewed raw rows:', len(recovered), '; reviewed pilot rows:', len(pilot_rows))
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mode', required=True,
                        choices=['prepare', 'recover', 'repair', 'check', 'status', 'publish'])
    args = parser.parse_args()
    if args.mode == 'prepare':
        prepare()
        return
    base.frozen(RUN)
    if args.mode == 'recover':
        recover()
        return
    if args.mode == 'repair':
        code = base.repair(RUN, 'es', target='full')
    elif args.mode == 'check':
        result = base.report(RUN, RUN / 'full')
        code = int(result['structural_failures'] != 0)
    elif args.mode == 'status':
        print(json.dumps(base.read_json(RUN / 'status.json'), ensure_ascii=False))
        code = 0
    else:
        code = base.publish(RUN, 'es')
    raise SystemExit(code)


if __name__ == '__main__':
    main()
