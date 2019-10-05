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

import com.ackdev.childDev.bean.GoalProgressBean;
import com.ackdev.childDev.jdo.GoalItemProgress;
import com.ackdev.common.dto.CommonResponse.Status;
import com.ackdev.store.UserStore;
import com.common.security.jdo.UserAccountV2;
import com.google.appengine.api.datastore.Text;

import testUtils.DataStoreTestHelper;
import testUtils.Helpers;

public class TestGoalProgressBean {
	private GoalProgressBean mGoalProgressBean = new GoalProgressBean();
	private UserStore mUserStore = new UserStore();
	private Long startTime = 0L;
	private Long endTime = 0L;

	@Before
	public void init() {
		DataStoreTestHelper.setupObjectify();
		UserAccountV2 userAccount = new UserAccountV2();
		userAccount.guid = "1234567890abcfghjkpqrstuvwxz";
		mUserStore.setUserAccount(userAccount);
		mGoalProgressBean.UT_InjectUserStore(mUserStore);
	}

	@After
	public void tearDown() {
		System.out.println("tearDown");
//		helper.tearDown();
		DataStoreTestHelper.closeObjectify();
	}

	@Test
	public void testBatch() {
		startTime = System.currentTimeMillis();
		List<GoalItemProgress> col = new ArrayList<GoalItemProgress>();
		for (int i = 1; i < 11; i++) {
			col.add(makeGoalItem());
		}
		endTime = System.currentTimeMillis();

		Collection<Status> rst = mGoalProgressBean.update(col);
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
		Collection<GoalItemProgress> batchGet = mGoalProgressBean.getRange(startTime, endTime);
		assertTrue(batchGet.size() == 10);
		for (GoalItemProgress ji : batchGet) {
			assertTrue(ji.guid != null && ji.guid.length() > 10);
			assertTrue(ji.accountFk != null && ji.accountFk.length() > 10);
		}
		for (GoalItemProgress ji : batchGet) {
			mGoalProgressBean.delete(ji.guid);
		}

	}

	@Test
	public void testUpdate() {
		GoalItemProgress GoalItemProgress = makeGoalItem();
		Status rst = mGoalProgressBean.update(GoalItemProgress);
		assertTrue(rst.isSuccess() == true);
		assertTrue(rst.guid != null && rst.guid.length() > 10);
		GoalItemProgress eval = mGoalProgressBean.get(rst.guid);
		assertTrue(rst.guid.equals(eval.guid));
		System.out.println(Helpers.mapCompare(GoalItemProgress, eval));
		assertTrue(Helpers.objDBCompare(GoalItemProgress, eval));
	}
	@Test
	public void testUpdateFail() {
		GoalItemProgress GoalItemProgress = makeGoalItem();
		GoalItemProgress.accountFk="1234567890abcfghjkpqrstuvwxzfail";
		Status rst = mGoalProgressBean.update(GoalItemProgress);
		assertFalse(rst.isSuccess() == true);
		assertTrue(rst.guid == null);

	}
	private GoalItemProgress makeGoalItem() {
		GoalItemProgress ret = new GoalItemProgress();
		ret.accountFk = "1234567890abcfghjkpqrstuvwxz";
		ret.enteredDate = System.currentTimeMillis();
		ret.notes = new Text(RandomStringUtils.random(100, "1234567890abcfghjkpqrstuvwxz"));
		ret.tags = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz") + " "
				+ RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");
		return ret;
	}
}
