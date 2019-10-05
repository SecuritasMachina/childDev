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

import com.ackdev.childDev.bean.GoalBean;
import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.common.dto.CommonResponse.Status;
import com.ackdev.store.UserStore;
import com.common.security.jdo.UserAccountV2;
import com.google.appengine.api.datastore.Text;

import testUtils.DataStoreTestHelper;
import testUtils.Helpers;

public class TestGoalBean {
	private GoalBean mGoalBean = new GoalBean();
	private UserStore mUserStore = new UserStore();
	private Long startTime = 0L;
	private Long endTime = 0L;

	@Before
	public void init() {
		DataStoreTestHelper.setupObjectify();
		UserAccountV2 userAccount = new UserAccountV2();
		userAccount.guid = "1234567890abcfghjkpqrstuvwxz";
		mUserStore.setUserAccount(userAccount);
		mGoalBean.UT_InjectUserStore(mUserStore);
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
		List<GoalItem> col = new ArrayList<GoalItem>();
		for (int i = 1; i < 11; i++) {
			col.add(makeGoalItem());
		}
		endTime = System.currentTimeMillis();

		Collection<Status> rst = mGoalBean.update(col);
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
		Collection<GoalItem> batchGet = mGoalBean.getRange(startTime, endTime);
		System.out.println("batchGet.size():"+batchGet.size());
		assertTrue(batchGet.size() == 10);
		for (GoalItem ji : batchGet) {
			assertTrue(ji.guid != null && ji.guid.length() > 10);
			assertTrue(ji.accountFk != null && ji.accountFk.length() > 10);
		}
		for (GoalItem ji : batchGet) {
			mGoalBean.delete(ji.guid);
		}

	}

	@Test
	public void testUpdate() {
		GoalItem GoalItem = makeGoalItem();
		Status rst = mGoalBean.update(GoalItem);
		assertTrue(rst.isSuccess() == true);
		assertTrue(rst.guid != null && rst.guid.length() > 10);
		GoalItem eval = mGoalBean.get(rst.guid);
		assertTrue(rst.guid.equals(eval.guid));
		System.out.println(Helpers.mapCompare(GoalItem, eval));
		assertTrue(Helpers.objDBCompare(GoalItem, eval));
	}
	@Test
	public void testUpdateFail() {
		GoalItem GoalItem = makeGoalItem();
		GoalItem.accountFk="1234567890abcfghjkpqrstuvwxzfail";
		Status rst = mGoalBean.update(GoalItem);
		assertFalse(rst.isSuccess() == true);
	
	}

	private GoalItem makeGoalItem() {
		GoalItem ret = new GoalItem();
		ret.accountFk = "1234567890abcfghjkpqrstuvwxz";
		ret.enteredDate = System.currentTimeMillis();
		ret.goalText = new Text(RandomStringUtils.random(100, "1234567890abcfghjkpqrstuvwxz"));
		ret.tags = RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz") + " "
				+ RandomStringUtils.random(10, "1234567890abcfghjkpqrstuvwxz");
		return ret;
	}
}
