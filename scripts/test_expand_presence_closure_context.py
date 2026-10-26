import unittest

import expand_presence_closure_context as expansion


def case(book, chapter, name='place'):
    key=f'KJV:{book}:{chapter}:1'
    return dict(case_id='test:0',source_kind='place',person='test',place_slug=None,place_name=name,
                category='present',original={},focus_keys=[key],candidate_places=[],
                verses=[dict(key=key,body='Exact original text',canonical=[book,chapter,1],
                    canonicalAddresses=[[book,chapter,1]],role='event')],
                context_metadata=dict(original_focus_keys=[key],bhsa={'status':'original'}))


class ExpandedContextTests(unittest.TestCase):
    def test_exodus_compartment_definitions_are_distant_context(self):
        wanted=expansion.requests(case(2,40),dict(group='tabernacle_room_identity'))
        self.assertEqual([row['canonical_chapter'] for row in wanted],[[2,26]])

    def test_huldah_and_bathsheba_parallels(self):
        huldah=expansion.requests(case(12,22),dict(group='mishneh_second_quarter'))
        self.assertEqual([row['canonical_chapter'] for row in huldah],[[14,34]])
        bathsheba=expansion.requests(case(13,3),dict(group='bathshua_bathsheba'))
        self.assertEqual([row['canonical_chapter'] for row in bathsheba],[[10,11],[10,12]])

    def test_passion_comparison_only_for_priestly_court_candidates(self):
        relevant=expansion.requests(case(43,18,'palace of the high priest'),dict(group='annas_caiaphas_sequence'))
        self.assertEqual({tuple(row['canonical_chapter']) for row in relevant},{(40,26),(41,14),(42,22)})
        unrelated=expansion.requests(case(43,18,'Kidron brook'),dict(group='scripture_context_ambiguous_location'))
        self.assertEqual(unrelated,[])

    def test_adding_a_definition_cannot_change_original_event(self):
        before=case(2,40)
        duplicate=dict(text='KJV',book=2,chapter=40,verse=1,label=None,body='Different source spelling',
                       canonical=[2,40,1],canonicalAddresses=[[2,40,1]])
        extra=dict(text='BHSA',book=2,chapter=26,verse=33,label=None,body='קֹ֖דֶשׁ הַקֳּדָשִֽׁים׃',
                   canonical=[2,26,33],canonicalAddresses=[[2,26,33]])
        after,added=expansion.merge(before,[duplicate,extra])
        self.assertEqual(after['focus_keys'],before['focus_keys'])
        self.assertEqual(next(v for v in after['verses'] if v['key']=='KJV:2:40:1'),before['verses'][0])
        hebrew=next(v for v in after['verses'] if v['key']=='BHSA:2:26:33')
        self.assertEqual(hebrew['body'],extra['body'])
        self.assertEqual(hebrew['role'],'context')
        self.assertEqual(added,['BHSA:2:26:33'])
        self.assertEqual(before['verses'][0]['body'],'Exact original text')

    def test_additional_web_parallel_remains_a_retrieval_hint(self):
        wanted=expansion.requests(case(40,4),dict(group='holy_city_parallel'))
        self.assertEqual([row['canonical_chapter'] for row in wanted],[[42,4]])
        self.assertEqual(expansion.requests(case(40,4),dict(group='scripture_context_ambiguous_event')),[])


if __name__=='__main__':
    unittest.main()
