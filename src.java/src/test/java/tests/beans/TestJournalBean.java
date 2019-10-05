package tests.beans;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;

import org.apache.commons.lang3.RandomStringUtils;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;

import com.ackdev.childDev.bean.JournalBean;
import com.ackdev.childDev.jdo.JournalItem;
import com.ackdev.common.dto.CommonResponse.Status;
import com.ackdev.store.UserStore;
import com.common.security.jdo.UserAccountV2;
import com.google.appengine.api.datastore.Text;

import testUtils.DataStoreTestHelper;
import testUtils.Helpers;

public class TestJournalBean {
	private JournalBean mJournalBean = new JournalBean();
	private UserStore mUserStore = new UserStore();
	private Long startTime = 0L;
	private Long endTime = 0L;

	@Before
	public void init() {
		DataStoreTestHelper.setupObjectify();
		UserAccountV2 userAccount = new UserAccountV2();
		userAccount.guid = "1234567890abcfghjkpqrstuvwxz";
		mUserStore.setUserAccount(userAccount);
		mJournalBean.UT_InjectUserStore(mUserStore);
	}

	@After
	public void tearDown() {
		System.out.println("tearDown");
		DataStoreTestHelper.closeObjectify();
	}

	@Test
	public void testBatch() {
		startTime = System.currentTimeMillis();
		List<JournalItem> col = new ArrayList<JournalItem>();
		for (int i = 1; i < 11; i++) {
			col.add(Helpers.makeJournalItem());
		}
		endTime = System.currentTimeMillis();

		Collection<Status> rst = mJournalBean.update(col);
		List<String> guidList = new ArrayList<String>();

		for (Status j : rst) {
			boolean foundIt = false;
			for (String guid : guidList) {
				if (j.guid.equals(guid)) {
					foundIt = true;
					break;
				}
			}
			assertFalse(foundIt);
			assertTrue(j.isSuccess());
			guidList.add(j.guid);
		}
		Collection<JournalItem> batchGet = mJournalBean.getRange(startTime, endTime);
		assertTrue(batchGet.size() == 10);
		for (JournalItem ji : batchGet) {
			assertTrue(ji.guid != null && ji.guid.length() > 10);
			assertTrue(ji.accountFk != null && ji.accountFk.length() > 10);
		}
		for (JournalItem ji : batchGet) {
			mJournalBean.delete(ji.guid);
		}

	}

	@Test
	public void testUpdate() {
		JournalItem journalItem = Helpers.makeJournalItem();
		Status rst = mJournalBean.update(journalItem);
		assertTrue(rst.isSuccess() == true);
		assertTrue(rst.guid != null && rst.guid.length() > 10);
		JournalItem eval = mJournalBean.get(rst.guid);
		assertTrue(rst.guid.equals(eval.guid));
		System.out.println(Helpers.mapCompare(journalItem, eval));
		assertTrue(Helpers.objDBCompare(journalItem, eval));
	}
	@Test
	public void testUpdateFail() {
	//	GoalItemProgress GoalItemProgress = makeGoalItem();
		JournalItem journalItem = Helpers.makeJournalItem();
		journalItem.accountFk="1234567890abcfghjkpqrstuvwxzfail";
		Status rst = mJournalBean.update(journalItem);
		assertFalse(rst.isSuccess() == true);
		assertTrue(rst.guid ==null);


	}

}
