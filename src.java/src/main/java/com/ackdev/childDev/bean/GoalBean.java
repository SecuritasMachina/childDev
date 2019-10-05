package com.ackdev.childDev.bean;

import static com.ackdev.common.application.CommonOfyService.ofyCommon;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.logging.Logger;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.common.dto.CommonResponse;
import com.ackdev.store.UserStore;

@Component
public class GoalBean {
	private static final Logger LOG = Logger.getLogger(GoalBean.class.getName());

	@Autowired
	private UserStore mUserStore;

	public CommonResponse.Status delete(String guid) {
		CommonResponse.Status ret = null;
		GoalItem goalItem = this.get(guid);
		if (goalItem.accountFk.equalsIgnoreCase(mUserStore.getUserAccount().guid)) {
			ofyCommon().delete().entity(goalItem).now();
			ret = new CommonResponse.Status().ok();
			ret.guid = guid;
		} else {
			// TODO log spoofing attempt
			ret = new CommonResponse.Status().fail();
		}
		return ret;
	}

	public GoalItem get(String guid) {
		GoalItem ret = ofyCommon().load().type(GoalItem.class).id(guid).now();
		if (ret.accountFk.equalsIgnoreCase(mUserStore.getUserAccount().guid)) {
			return ret;
		} else {
			// TODO log spoofing attempt
		}
		return null;
	}

	public CommonResponse.Status update(GoalItem goalItem) {
		CommonResponse.Status ret = null;
		if (goalItem.accountFk != null && goalItem.accountFk.equalsIgnoreCase(mUserStore.getUserAccount().guid)) {
			ofyCommon().save().entity(goalItem).now();
			ret = new CommonResponse.Status().ok();
			ret.guid = goalItem.guid;

		} else {
			// TODO log spoofing attempt
			LOG.warning("spoofing attempt");
			ret = new CommonResponse.Status().fail();
		}

		return ret;
	}

	public Collection<CommonResponse.Status> update(Collection<GoalItem> journalCollection) {
		ArrayList<CommonResponse.Status> ret = new ArrayList<>();
		for (GoalItem j : journalCollection) {
			CommonResponse.Status e = this.update(j);
			ret.add(e);
		}
		return ret;
	}

	public Collection<GoalItem> getRange(Long startTS, Long endTS) {
		List<GoalItem> eList = ofyCommon().load().type(GoalItem.class)
				.filter("accountFk", mUserStore.getUserAccount().guid).filter("enteredDate >=", startTS)
				.filter("enteredDate <=", endTS).list();// .first()
		return eList;
	}

	public void UT_InjectUserStore(UserStore userStore) {
		this.mUserStore = userStore;
	}

}
